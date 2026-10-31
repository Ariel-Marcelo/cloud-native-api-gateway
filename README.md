# 🚀 Enterprise Polyglot API Gateway (.NET 8, YARP, Spring Boot & Redis)

Una arquitectura de **API Gateway Cloud-Native** de alto rendimiento construida con **ASP.NET Core (.NET 8)** y **Microsoft YARP**, diseñada para orquestar, proteger y escalar un ecosistema de microservicios políglota (**.NET 8** y **Java 17 / Spring Boot**) utilizando **Redis** para control de tráfico distribuido y **Docker Compose** para aislamiento de red.

---

## 🏛️ Arquitectura del Sistema

```mermaid
flowchart TD
    subgraph Clientes["Clientes Externos"]
        Client["Web / Mobile / Postman"]
    end

    subgraph DockerNet["Red Privada Docker (gateway-network)"]
        subgraph Edge["API Gateway (.NET 8 + YARP) — Puerto Público 5000"]
            FH["1. ForwardedHeaders Middleware<br/>(Anti-IP Spoofing)"]
            CID["2. Correlation ID Middleware<br/>(Trazabilidad Distribuida)"]
            JWT["3. JWT Bearer Authentication<br/>(Validación Criptográfica HS256)"]
            RL["4. Dynamic Tier Rate Limiter<br/>(Free vs Premium + IOptionsMonitor)"]
            AUTHZ["5. Route Authorization Policies<br/>(Anonymous vs Authenticated)"]
            CACHE["6. Distributed Response Caching<br/>(X-Cache: HIT/MISS + Stream Swapping)"]
            YARP["7. YARP Reverse Proxy<br/>• Claim-to-Header Offloading<br/>• RoundRobin Load Balancing<br/>• Active & Passive Health Checks"]
            
            FH --> CID --> JWT --> RL --> AUTHZ --> CACHE --> YARP
        end

        subgraph State["Estado Distribuido"]
            REDIS[("Redis 7 Alpine<br/>• Sorted Sets (ZSET) + Lua<br/>• Strings + TTL (Response Cache)")]
        end

        subgraph Downstream["Microservicios Internos (Sin Puertos Públicos)"]
            AUTH["AuthService (Spring Boot 4 / Java 17)<br/>Emisor de Tokens JWT (:8085)"]
            CAT1["CatalogService Nodo 1 (.NET 8)<br/>(:8080)"]
            CAT2["CatalogService Nodo 2 (.NET 8)<br/>(:8082)"]
            ORD["OrdersService (Spring Boot 4 / Java 17)<br/>(:8083)"]
        end
    end

    Client -->|"HTTP :5000"| FH
    RL <-->|"Sliding Window Atómico"| REDIS
    CACHE <-->|"GET / SET EX (TTL 30s)"| REDIS
    YARP -->|"POST /auth/login (Público)"| AUTH
    YARP -->|"Balanceo RoundRobin + /healthz"| CAT1
    YARP -->|"Balanceo RoundRobin + /healthz"| CAT2
    YARP -->|"Proxy + Headers X-User-*"| ORD
```

---

## ✨ Características de Nivel Producción Implementadas

### 1. Seguridad Centralizada y *JWT Gateway Offloading*
* **Separación Emisor / Validador:** Un microservicio dedicado en **Java Spring Boot (`AuthService`)** autentica credenciales y firma tokens JWT (`HS256`) con `sub`, `username`, `role`, `tier`, `iss`, `aud` y `exp`.
* **Validación en el Borde (Edge Authentication):** El API Gateway valida criptográficamente el token con `ClockSkew = TimeSpan.Zero` y `MapInboundClaims = false`. Las peticiones no autorizadas son rechazadas con `401 Unauthorized` en el Gateway sin consumir recursos de los microservicios internos.
* **Prevención de *Header Spoofing* e Inyección de Contexto:** Mediante `RequestTransforms` de YARP, el Gateway elimina cualquier cabecera `X-User-*` maliciosa proveniente del exterior e inyecta los claims validados (`X-User-Id`, `X-User-Name`, `X-User-Role`, `X-User-Tier`) hacia los microservicios downstream.

### 2. Rate Limiting Distribuido Dinámico por *Tier* (`Free` vs `Premium`)
* **Algoritmo *Sliding Window Counter* en Redis con Lua:** Ejecución atómica en un único *round-trip* de red (`ZREMRANGEBYSCORE` + `ZCARD` + `ZADD` + `EXPIRE`), eliminando condiciones de carrera (*race conditions*) entre múltiples instancias del Gateway.
* **Cuotas Dinámicas Basadas en Claims:**
  * Usuarios autenticados se particionan en Redis por identidad (`ratelimit:sliding:user:{sub}`) y reciben la cuota correspondiente a su claim `tier` (`Free`: 5 req/10s, `Premium`: 10+ req/10s).
  * Usuarios anónimos (ej. `/auth/login`) se particionan por IP (`ratelimit:sliding:ip:{ip}`) bajo el `DefaultTier`, protegiendo el servicio de autenticación contra ataques de fuerza bruta.
* **Recarga en Caliente (`IOptionsMonitor`) y Resiliencia (*Fail-Open*):** Las cuotas por Tier pueden ajustarse en `appsettings.json` en tiempo real sin reiniciar el Gateway. Si Redis experimenta una caída transitoria, el Gateway degrada elegantemente permitiendo el paso del tráfico.

### 3. Alta Disponibilidad, Balanceo de Carga y Health Checks Híbridos
* **Escalado Horizontal:** `CatalogService` opera en un clúster de múltiples réplicas balanceadas mediante política **`RoundRobin`**.
* **Active + Passive Health Checks en YARP:**
  * **Sondeo Activo (`ConsecutiveFailures`):** YARP verifica proactivamente el endpoint `/healthz` de cada contenedor (.NET y Spring Boot) cada 10 segundos. Si una réplica cae, se retira automáticamente del clúster.
  * **Sondeo Pasivo (`TransportFailureRate`):** Monitorea la tasa de fallos de transporte en el tráfico real para aislar nodos degradados entre intervalos de sondeo activo y reactivarlos tras su periodo de enfriamiento (`ReactivationPeriod`).

### 4. Observabilidad y Trazabilidad Distribuida Políglota
* **Propagación de `X-Correlation-Id` de Extremo a Extremo:** El Gateway genera o preserva el identificador de correlación en cada solicitud y lo propaga a todos los microservicios.
* **Logging Estructurado Homogéneo:** Implementado mediante `ILogger.BeginScope` con `IncludeScopes` en **.NET 8** y `OncePerRequestFilter` + `SLF4J MDC` (`logback-spring.xml`) en **Java Spring Boot**, permitiendo rastrear una transacción completa a través de distintos lenguajes de programación.

### 5. Response Caching Distribuido en el Borde (`X-Cache: HIT` / `MISS`)
* **Cortocircuito en Memoria RAM con Redis:** Para rutas de alta lectura configurables (ej. `GET /catalog/products`), el Gateway consulta Redis antes de reenviar la solicitud a los microservicios downstream.
* **Reducción Drástica de Latencia (`X-Cache: HIT`):** Si la respuesta existe en Redis dentro de la ventana de vida (`DefaultTtlSeconds: 30`), el Gateway responde directamente desde la RAM con el `Content-Type` y `Body` originales sin golpear los contenedores de `CatalogService` ni sus bases de datos.
* **Intercepción Transparente de Streams (`X-Cache: MISS`):** Si la clave no existe, el middleware intercepta el stream de respuesta de YARP mediante *MemoryStream Swapping*, almacena el payload en Redis con expiración automática (`SET key value EX ttl`) únicamente cuando el microservicio responde `200 OK`, y devuelve los bytes al cliente con resiliencia *Fail-Open*.

---

## 🛠️ Stack Tecnológico

| Capa | Tecnología | Rol en la Arquitectura |
| :--- | :--- | :--- |
| **API Gateway** | .NET 8 (C# 12), Microsoft YARP 2.3 | Enrutamiento inverso, Auth Offloading, Caché, Balanceo, Health Checks |
| **Estado Distribuido** | Redis 7 Alpine, StackExchange.Redis, Lua | Ventanas deslizantes (Rate Limit) y Response Caching distribuido con TTL |
| **Identidad (`AuthService`)** | Java 17, Spring Boot 4, JJWT (`0.13.0`) | Autenticación y emisión de tokens JWT firmados (`HS256`) |
| **Catálogo (`CatalogService`)** | .NET 8 Minimal APIs (2 Réplicas) | Microservicio de productos con soporte de alta disponibilidad |
| **Órdenes (`OrdersService`)** | Java 17, Spring Boot 4, Lombok | Microservicio de pedidos protegido por políticas del Gateway |
| **Infraestructura** | Docker & Docker Compose | Contenerización multi-stage y aislamiento en red privada |

---

## 📂 Estructura del Repositorio

```text
ApiGateway/
├── docker-compose.yml              # Orquestación de Redis, Gateway y 4 contenedores de microservicios
├── requests.http                   # Suite de pruebas HTTP listas para ejecutar
├── README.md                       # Documentación general
├── Notes.md                        # Bitácora personal de arquitectura y lecciones aprendidas
└── src/
    ├── Gateway/
    │   └── ApiGateway/             # API Gateway (.NET 8 + YARP)
    │       ├── Configuration/      # Opciones tipadas (RateLimitingOptions, ResponseCachingOptions)
    │       ├── Middlewares/        # CorrelationId, DistributedRateLimiting y DistributedResponseCaching
    │       ├── Services/           # RedisRateLimiterService y RedisResponseCachedService
    │       ├── Program.cs          # Pipeline de seguridad, ForwardedHeaders, Caché y Transforms de YARP
    │       └── appsettings.json    # Rutas, Clusters, HealthChecks, Caché y Tiers de Rate Limiting
    └── Services/
        ├── AuthService/            # Microservicio Emisor de JWT (Java 17 / Spring Boot)
        ├── CatalogService/         # Microservicio de Catálogo (.NET 8 - Escalado a 2 nodos)
        └── OrdersService/          # Microservicio de Órdenes (Java 17 / Spring Boot)
```

---

## 🚀 Cómo Ejecutar el Proyecto

### Prerrequisitos
* [Docker Desktop](https://www.docker.com/products/docker-desktop/) activo.

### 1. Levantar toda la infraestructura
Desde la raíz del repositorio:

```bash
docker compose up -d --build
```

| Contenedor | Tecnología | Puerto | Acceso |
| :--- | :--- | :--- | :--- |
| `apigateway-proxy` | .NET 8 + YARP | **`5000:8080`** | **Público** (`http://localhost:5000`) |
| `apigateway-redis` | Redis 7 Alpine | `6379:6379` | Almacén de estado |
| `apigateway-auth-service` | Spring Boot | `8085` | Privado (Solo vía Gateway `/auth/*`) |
| `apigateway-catalog-service` | .NET 8 (Nodo 1) | `8080` | Privado (Solo vía Gateway `/catalog/*`) |
| `apigateway-catalog-service-2` | .NET 8 (Nodo 2) | `8082` | Privado (Solo vía Gateway `/catalog/*`) |
| `apigateway-orders-service` | Spring Boot | `8083` | Privado (Solo vía Gateway `/orders/*`) |

---

## 🧪 Guía de Pruebas Rápidas

### 1. Verificar protección de rutas (`401 Unauthorized`)
```bash
curl -i http://localhost:5000/orders
```
*El Gateway bloquea la petición inmediatamente al no incluir un JWT válido.*

### 2. Obtener un Token JWT (`Free` vs `Premium`)
* **Usuario `Premium` (`Juan` / `hash1`):**
  ```bash
  curl -i -X POST http://localhost:5000/auth/login \
    -H "Content-Type: application/json" \
    -d '{"username":"Juan","password":"hash1"}'
  ```
* **Usuario `Free` (`Maria` / `hash2`):**
  ```bash
  curl -i -X POST http://localhost:5000/auth/login \
    -H "Content-Type: application/json" \
    -d '{"username":"Maria","password":"hash2"}'
  ```

### 3. Consumir microservicios protegidos y verificar Rate Limiting por Tier
```bash
curl -i http://localhost:5000/orders \
  -H "Authorization: Bearer <TU_JWT_TOKEN>"
```
Observa los encabezados de respuesta inyectados por el Gateway:
* `X-Correlation-Id`: ID único propagado hasta los logs de Java/.NET.
* `X-RateLimit-Limit`: `10` (si usaste el token de `Juan` - `Premium`) o `5` (si usaste el de `Maria` - `Free`).
* `X-RateLimit-Remaining`: Peticiones disponibles en la ventana deslizante de 10 segundos.

### 4. Probar Response Caching Distribuido (`X-Cache: MISS` vs `HIT`)
Realiza dos peticiones seguidas en menos de 30 segundos al catálogo:
```bash
curl -i http://localhost:5000/catalog/products \
  -H "Authorization: Bearer <TU_JWT_TOKEN>"
```
* **1ra Petición:** Devuelve `X-Cache: MISS` (YARP consulta a `CatalogService` y almacena la respuesta en Redis por 30s).
* **2da Petición (dentro de los 30s):** Devuelve `X-Cache: HIT` directamente desde la memoria de Redis en menos de 2ms sin tocar los contenedores del microservicio.

### 5. Probar Failover Automático (Health Checks de YARP)
Detén una de las réplicas de catálogo:
```bash
docker stop apigateway-catalog-service-2
```
Realiza múltiples peticiones a `GET http://localhost:5000/catalog/products` con tu token: YARP detecta la caída mediante `/healthz` y redirige el $100\%$ del tráfico hacia el nodo sano sin errores `502` para el cliente.

---

## 📄 Licencia
Distribuido bajo la licencia MIT.
