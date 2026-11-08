<#
.SYNOPSIS
    Simulador de Trafico Automatizado para el API Gateway (.NET 8 + YARP + Redis + Observabilidad).
.DESCRIPTION
    Ejecuta una coreografia reproducible de escenarios para poblar en tiempo real
    el Dashboard de Grafana (http://localhost:3000) y el visor RedisInsight (http://localhost:5540):
      1. Seguridad en el Borde (401 Unauthorized sin JWT)
      2. Emision de JWT RS256 en AuthService (Tiers: Free vs Premium)
      3. Response Caching en Ruta Publica (/api/v1/catalog/products -> cached:/api/v1/catalog/products) y Balanceo YARP
      4. Rate Limiting Distribuido con Lua (5 req/10s -> 200 OK y 429 Too Many Requests)
      5. Response Caching Zero-Trust Aislado por Usuario (/api/v1/orders?delay=0 -> cached:{sub}:/api/v1/orders?delay=0 + Vary: Authorization)
      6. Resiliencia: ActivityTimeout (504) y Expulsion Pasiva de Destino (503 en 0ms)
      7. Pulso final para mantener llaves activas en RedisInsight (ZSET + STRING publico y por usuario con TTL)
.PARAMETER BaseUrl
    URL publica del API Gateway (por defecto: http://localhost:5000).
.PARAMETER Continuous
    Si se especifica, mantiene un pulso continuo al finalizar para tomar capturas sin que expiren los TTLs en Redis.
.EXAMPLE
    .\simulate-traffic.ps1
.EXAMPLE
    .\simulate-traffic.ps1 -Continuous
#>

param(
    [string]$BaseUrl = "http://localhost:5000",
    [switch]$Continuous
)

$ErrorActionPreference = "Stop"

function Write-Header([string]$Title) {
    Write-Host ""
    Write-Host "================================================================================" -ForegroundColor Cyan
    Write-Host " $Title" -ForegroundColor Cyan
    Write-Host "================================================================================" -ForegroundColor Cyan
}

function Invoke-GatewayRequest {
    param(
        [string]$Method = "GET",
        [string]$Path,
        [string]$Token = "",
        [string]$Body = "",
        [string]$CorrelationId = ""
    )

    $uri = "$BaseUrl$Path"
    $sw = [System.Diagnostics.Stopwatch]::StartNew()

    $req = [System.Net.HttpWebRequest]::Create($uri)
    $req.Method = $Method
    $req.Accept = "application/json"
    $req.Timeout = 15000

    if ($Token) {
        $req.Headers.Add("Authorization", "Bearer $Token")
    }
    if ($CorrelationId) {
        $req.Headers.Add("X-Correlation-Id", $CorrelationId)
    }

    if ($Body -and ($Method -eq "POST" -or $Method -eq "PUT")) {
        $req.ContentType = "application/json"
        $bytes = [System.Text.Encoding]::UTF8.GetBytes($Body)
        $req.ContentLength = $bytes.Length
        $stream = $req.GetRequestStream()
        $stream.Write($bytes, 0, $bytes.Length)
        $stream.Close()
    }

    $statusCode = 0
    $headers = @{}
    $responseBody = ""

    try {
        $resp = [System.Net.HttpWebResponse]$req.GetResponse()
        $sw.Stop()
        $statusCode = [int]$resp.StatusCode
        foreach ($key in $resp.Headers.AllKeys) {
            $headers[$key] = $resp.Headers[$key]
        }
        $reader = New-Object System.IO.StreamReader($resp.GetResponseStream())
        $responseBody = $reader.ReadToEnd()
        $reader.Close()
        $resp.Close()
    }
    catch [System.Net.WebException] {
        $sw.Stop()
        if ($_.Exception.Response) {
            $errResp = [System.Net.HttpWebResponse]$_.Exception.Response
            $statusCode = [int]$errResp.StatusCode
            foreach ($key in $errResp.Headers.AllKeys) {
                $headers[$key] = $errResp.Headers[$key]
            }
            $reader = New-Object System.IO.StreamReader($errResp.GetResponseStream())
            $responseBody = $reader.ReadToEnd()
            $reader.Close()
            $errResp.Close()
        }
        else {
            throw
        }
    }

    return [PSCustomObject]@{
        StatusCode    = $statusCode
        ElapsedMs     = $sw.ElapsedMilliseconds
        Cache         = $headers["X-Cache"]
        Vary          = $headers["Vary"]
        RateLimit     = $headers["X-RateLimit-Limit"]
        RateRemaining = $headers["X-RateLimit-Remaining"]
        RetryAfter    = $headers["Retry-After"]
        CorrelationId = $headers["X-Correlation-Id"]
        Body          = $responseBody
    }
}

Write-Header "[START] SIMULACION DE TRAFICO - API GATEWAY (.NET 8 + YARP + REDIS)"
Write-Host "Gateway URL  : $BaseUrl" -ForegroundColor Gray
Write-Host "Grafana      : http://localhost:3000 (admin / admin - solo demo)" -ForegroundColor Gray
Write-Host "Prometheus   : http://localhost:9090" -ForegroundColor Gray
Write-Host "RedisInsight : http://localhost:5540 (redis://default@redis:6379)" -ForegroundColor Gray

# ------------------------------------------------------------------------------
# 0. Verificar que el API Gateway esta en linea
# ------------------------------------------------------------------------------
try {
    $health = Invoke-GatewayRequest -Method "GET" -Path "/healthz"
    if ($health.StatusCode -ne 200) {
        Write-Host ("[ERROR] El API Gateway respondio con estado {0}. Ejecuta 'docker compose up -d'." -f $health.StatusCode) -ForegroundColor Red
        exit 1
    }
    Write-Host ("[OK] API Gateway operativo (/healthz -> 200 OK en {0} ms)" -f $health.ElapsedMs) -ForegroundColor Green
}
catch {
    Write-Host ("[ERROR] No se pudo conectar a {0}. Asegurate de ejecutar 'docker compose up -d'." -f $BaseUrl) -ForegroundColor Red
    exit 1
}

# ------------------------------------------------------------------------------
# FASE 1: Seguridad en el Borde (401 Unauthorized)
# ------------------------------------------------------------------------------
Write-Header "[FASE 1] Seguridad en el Borde - Rechazo sin Token JWT (401)"
for ($i = 1; $i -le 3; $i++) {
    $res = Invoke-GatewayRequest -Method "GET" -Path "/api/v1/orders" -CorrelationId "demo-unauth-00$i"
    Write-Host ("  [Req #{0}] GET /api/v1/orders (Sin Token) -> HTTP {1} ({2} ms) | X-Correlation-Id: {3}" -f `
        $i, $res.StatusCode, $res.ElapsedMs, $res.CorrelationId) -ForegroundColor Yellow
    Start-Sleep -Milliseconds 150
}

# ------------------------------------------------------------------------------
# FASE 2: Autenticacion en AuthService (Obtencion de JWTs RS256 Free y Premium)
# ------------------------------------------------------------------------------
Write-Header "[FASE 2] Emision de Tokens JWT RS256 en AuthService (Spring Boot)"

$loginPremium = Invoke-GatewayRequest -Method "POST" -Path "/api/v1/auth/login" `
    -Body '{"username":"Juan","password":"hash1"}' -CorrelationId "demo-auth-premium"
$tokenPremium = $loginPremium.Body.Trim().Trim('"')
Write-Host ("  [Premium] Usuario 'Juan'  (sub: 1) -> HTTP {0} ({1} ms) | Tier: Premium (10 req/10s)" -f $loginPremium.StatusCode, $loginPremium.ElapsedMs) -ForegroundColor Green

$loginFree = Invoke-GatewayRequest -Method "POST" -Path "/api/v1/auth/login" `
    -Body '{"username":"Maria","password":"hash2"}' -CorrelationId "demo-auth-free"
$tokenFree = $loginFree.Body.Trim().Trim('"')
Write-Host ("  [Free]    Usuario 'Maria' (sub: 2) -> HTTP {0} ({1} ms) | Tier: Free    (5 req/10s)" -f $loginFree.StatusCode, $loginFree.ElapsedMs) -ForegroundColor Green

# ------------------------------------------------------------------------------
# FASE 3: Response Caching en Ruta Publica (PublicRoutes) y Balanceo YARP
# ------------------------------------------------------------------------------
Write-Header "[FASE 3] Response Caching en Ruta Publica (cached:/api/v1/catalog/products)"
for ($i = 1; $i -le 6; $i++) {
    $res = Invoke-GatewayRequest -Method "GET" -Path "/api/v1/catalog/products" -Token $tokenPremium -CorrelationId "demo-cache-00$i"
    $color = if ($res.Cache -eq "HIT") { "Green" } else { "Cyan" }
    Write-Host ("  [Req #{0}] GET /api/v1/catalog/products -> HTTP {1} ({2,3} ms) | X-Cache: {3,-4} | Remaining: {4}/{5}" -f `
        $i, $res.StatusCode, $res.ElapsedMs, $res.Cache, $res.RateRemaining, $res.RateLimit) -ForegroundColor $color
    Start-Sleep -Milliseconds 200
}

# Peticiones hacia CatalogService por ID para mostrar balanceo RoundRobin entre replicas
Write-Host ""
Write-Host "  -> Generando trafico no cacheado hacia el cluster CatalogService (RoundRobin entre 2 nodos)..." -ForegroundColor Gray
for ($i = 1; $i -le 4; $i++) {
    $res = Invoke-GatewayRequest -Method "GET" -Path "/api/v1/catalog/products/11111111-1111-1111-1111-111111111111" -Token $tokenPremium
    Write-Host ("  [Node LB #{0}] GET /api/v1/catalog/products/{{id}} -> HTTP {1} ({2,3} ms) | Remaining: {3}/{4}" -f `
        $i, $res.StatusCode, $res.ElapsedMs, $res.RateRemaining, $res.RateLimit) -ForegroundColor DarkCyan
    Start-Sleep -Milliseconds 150
}

# ------------------------------------------------------------------------------
# FASE 4: Rate Limiting Distribuido por Tier (Sliding Window Log en Redis + Lua)
# ------------------------------------------------------------------------------
Write-Header "[FASE 4] Rate Limiting Distribuido - Rafaga Usuario Free (5 req/10s -> 429)"
for ($i = 1; $i -le 9; $i++) {
    $res = Invoke-GatewayRequest -Method "GET" -Path "/api/v1/catalog/products" -Token $tokenFree -CorrelationId "demo-ratelimit-00$i"
    if ($res.StatusCode -eq 429) {
        Write-Host ("  [Req #{0}] GET /api/v1/catalog/products -> HTTP {1} TOO MANY REQUESTS ({2,2} ms) | Retry-After: {3}s" -f `
            $i, $res.StatusCode, $res.ElapsedMs, $res.RetryAfter) -ForegroundColor Red
        # Tras el primer 429, esperar 5s (1 scrape de Prometheus) para que rate()/increase() tenga 2 muestras crecientes
        if ($i -eq 6) {
            Start-Sleep -Seconds 5
        }
    }
    else {
        Write-Host ("  [Req #{0}] GET /api/v1/catalog/products -> HTTP {1} OK                ({2,2} ms) | Remaining: {3}/{4} | X-Cache: {5}" -f `
            $i, $res.StatusCode, $res.ElapsedMs, $res.RateRemaining, $res.RateLimit, $res.Cache) -ForegroundColor Green
    }
    Start-Sleep -Milliseconds 100
}

# ------------------------------------------------------------------------------
# FASE 5: Response Caching Zero-Trust Aislado por Usuario (/api/v1/orders?delay=0)
# ------------------------------------------------------------------------------
Write-Header "[FASE 5] Caché Zero-Trust Aislado por Usuario (cached:{sub}:/api/v1/orders?delay=0)"
Write-Host "  Esperando 10s para renovar las cuotas de Rate Limit antes de probar Orders..." -ForegroundColor DarkGray
Start-Sleep -Seconds 10

# Juan (sub=1): 1ra llamada MISS (crea cached:1:/api/v1/orders?delay=0), 2da llamada HIT
for ($i = 1; $i -le 2; $i++) {
    $res = Invoke-GatewayRequest -Method "GET" -Path "/api/v1/orders?delay=0" -Token $tokenPremium -CorrelationId "demo-priv-juan-00$i"
    $color = if ($res.Cache -eq "HIT") { "Green" } else { "Cyan" }
    Write-Host ("  [Juan  (sub:1) #{0}] GET /api/v1/orders?delay=0 -> HTTP {1} ({2,3} ms) | X-Cache: {3,-4} | Vary: {4}" -f `
        $i, $res.StatusCode, $res.ElapsedMs, $res.Cache, $res.Vary) -ForegroundColor $color
    Start-Sleep -Milliseconds 150
}

# Maria (sub=2): 1ra llamada recibe MISS (aislada de Juan, crea cached:2:/api/v1/orders?delay=0), 2da llamada HIT
for ($i = 1; $i -le 2; $i++) {
    $res = Invoke-GatewayRequest -Method "GET" -Path "/api/v1/orders?delay=0" -Token $tokenFree -CorrelationId "demo-priv-maria-00$i"
    $color = if ($res.Cache -eq "HIT") { "Green" } else { "Cyan" }
    Write-Host ("  [Maria (sub:2) #{0}] GET /api/v1/orders?delay=0 -> HTTP {1} ({2,3} ms) | X-Cache: {3,-4} | Vary: {4}" -f `
        $i, $res.StatusCode, $res.ElapsedMs, $res.Cache, $res.Vary) -ForegroundColor $color
    Start-Sleep -Milliseconds 150
}

# ------------------------------------------------------------------------------
# FASE 6: Resiliencia - ActivityTimeout (504) y Expulsion Pasiva (503 en 0ms)
# ------------------------------------------------------------------------------
Write-Header "[FASE 6] Resiliencia - Timeout (2s -> 504) y Expulsion Pasiva (5 fallos -> 503 en 0ms)"

for ($i = 1; $i -le 7; $i++) {
    # Llamada sin query string (?delay=5000 por defecto en OrdersService) -> corta a los 2s con 504 (no se cachea por no ser 200 OK)
    $res = Invoke-GatewayRequest -Method "GET" -Path "/api/v1/orders" -Token $tokenPremium -CorrelationId "demo-cb-00$i"
    if ($res.StatusCode -eq 504) {
        Write-Host ("  [Intento #{0}] GET /api/v1/orders (Delay 5s) -> HTTP 504 GATEWAY TIMEOUT     ({1,4} ms) [Cortado a los 2s por YARP]" -f `
            $i, $res.ElapsedMs) -ForegroundColor Yellow
    }
    elseif ($res.StatusCode -eq 503) {
        Write-Host ("  [Intento #{0}] GET /api/v1/orders            -> HTTP 503 SERVICE UNAVAILABLE ({1,4} ms) [Rechazo instantaneo 0ms - RFC 7807]" -f `
            $i, $res.ElapsedMs) -ForegroundColor Magenta
    }
    else {
        Write-Host ("  [Intento #{0}] GET /api/v1/orders            -> HTTP {1} ({2,4} ms)" -f `
            $i, $res.StatusCode, $res.ElapsedMs) -ForegroundColor Cyan
    }
}

# ------------------------------------------------------------------------------
# FASE 7: Pulso Final para Capturas en RedisInsight y Grafana
# ------------------------------------------------------------------------------
Write-Header "[FASE 7] Preparando Estado Activo en RedisInsight y Grafana"
Write-Host "  Refrescando llaves en Redis (cached:/api/v1/catalog/products, cached:{sub}:/api/v1/orders?delay=0 y ratelimit:sliding:user:*)..." -ForegroundColor Gray

# Refrescar cache publico y disparar rafaga 429 para que Prometheus y Grafana muestren el pico
[void](Invoke-GatewayRequest -Method "GET" -Path "/api/v1/catalog/products" -Token $tokenPremium)
for ($i = 1; $i -le 7; $i++) {
    [void](Invoke-GatewayRequest -Method "GET" -Path "/api/v1/catalog/products" -Token $tokenFree)
}

Write-Host ""
Write-Host "[DONE] SIMULACION COMPLETADA CON EXITO." -ForegroundColor Green
Write-Host "   1. Abre Grafana      -> http://localhost:3000 (Dashboard: 'API Gateway & Redis - Observability')" -ForegroundColor White
Write-Host "   2. Abre RedisInsight -> http://localhost:5540 (Veras tanto 'cached:/api/v1/catalog/products' como 'cached:1:/api/v1/orders?delay=0' y 'cached:2:...')" -ForegroundColor White

if ($Continuous) {
    Write-Host ""
    Write-Host "[LOOP] MODO CONTINUO ACTIVO (Presiona Ctrl+C para detener)..." -ForegroundColor Cyan
    Write-Host "   Manteniendo vivas en Redis tanto la clave publica (catalog) como las claves por usuario (orders: 1 y 2)..." -ForegroundColor Gray
    $iter = 1
    while ($true) {
        # 1. Mantener vivo el cache publico (/api/v1/catalog/products) y el cache privado por usuario (/api/v1/orders?delay=0)
        $rPub   = Invoke-GatewayRequest -Method "GET" -Path "/api/v1/catalog/products" -Token $tokenPremium
        $rOrdA  = Invoke-GatewayRequest -Method "GET" -Path "/api/v1/orders?delay=0" -Token $tokenPremium
        $rOrdB  = Invoke-GatewayRequest -Method "GET" -Path "/api/v1/orders?delay=0" -Token $tokenFree

        # 2. Generar rafaga con Free (6 peticiones para garantizar respuestas 429 continuas en Grafana)
        for ($j = 1; $j -le 6; $j++) {
            $rBurst = Invoke-GatewayRequest -Method "GET" -Path "/api/v1/catalog/products" -Token $tokenFree
        }

        Write-Host ("  [Pulso #{0}] Catalog(Pub): {1} ({2}) | Orders(1): {3} ({4}) | Orders(2): {5} ({6}) | Free Burst: {7}" -f `
            $iter, $rPub.StatusCode, $rPub.Cache, $rOrdA.StatusCode, $rOrdA.Cache, $rOrdB.StatusCode, $rOrdB.Cache, $rBurst.StatusCode) -ForegroundColor DarkGray
        $iter++
        Start-Sleep -Seconds 3
    }
}
