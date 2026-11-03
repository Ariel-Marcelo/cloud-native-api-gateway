package com.organization.auth;

import io.jsonwebtoken.Jwts;
import jakarta.annotation.PostConstruct;
import lombok.AllArgsConstructor;
import lombok.Data;
import lombok.NoArgsConstructor;
import lombok.extern.slf4j.Slf4j;
import org.springframework.http.HttpStatus;
import org.springframework.http.ResponseEntity;
import org.springframework.web.bind.annotation.*;

import java.math.BigInteger;
import java.security.interfaces.RSAPublicKey;
import java.time.Instant;
import java.util.*;

@Slf4j
@RestController
@RequestMapping("/api/v1/authentication")
public class AuthController {

    private final JwtConfig jwtConfig;
    private final Map<String, UserInfo> usersByUsername = new HashMap<>();

    public AuthController(JwtConfig jwtConfig) {
        this.jwtConfig = jwtConfig;
    }

    @PostConstruct
    public void init() {
        usersByUsername.put("Juan", new UserInfo("1", "Juan", "hash1", "Admin", "Premium"));
        usersByUsername.put("Maria", new UserInfo("2", "Maria", "hash2", "Customer", "Free"));
    }

    @PostMapping("/login")
    public ResponseEntity<String> auth(@RequestBody LoginRequest req) {
        log.info("Authentication Init for user '{}'", req.getUsername());
        UserInfo userInfo = usersByUsername.get(req.getUsername());
        if (userInfo == null || !userInfo.getPassword().equals(req.getPassword()) || userInfo.role.isBlank() || userInfo.tier.isBlank()) {
            log.warn("Authentication Failed for user '{}'", req.getUsername());
            return ResponseEntity.status(HttpStatus.UNAUTHORIZED).build();
        }

        log.info("Generating RS256 JWT Token for user '{}' with kid '{}'", userInfo.getUsername(), jwtConfig.getKeyId());
        String role = userInfo.role;
        String tier = userInfo.tier;

        try {
            return ResponseEntity.ok(Jwts.builder()
                    .header().keyId(jwtConfig.getKeyId()).and()
                    .subject(userInfo.id)
                    .issuer(jwtConfig.getIssuer())
                    .audience().add(jwtConfig.getAudience()).and()
                    .issuedAt(Date.from(Instant.now()))
                    .expiration(Date.from(Instant.now().plusMillis(jwtConfig.getExpirationMs())))
                    .claim("username", userInfo.username)
                    .claim("role", role)
                    .claim("tier", tier)
                    .signWith(jwtConfig.getPrivateKey(), Jwts.SIG.RS256)
                    .compact());
        } catch (Exception e) {
            log.error("Authentication Failed Exception for user '{}'", req.getUsername(), e);
            return ResponseEntity.badRequest().build();
        }
    }

    /**
     * Endpoint estándar RFC 7517 (JSON Web Key Set - JWKS).
     * Expone únicamente la clave pública RSA para que el API Gateway valide tokens RS256
     * sin compartir ningún secreto simétrico.
     */
    @GetMapping("/.well-known/jwks.json")
    public ResponseEntity<Map<String, Object>> jwks() {
        RSAPublicKey publicKey = jwtConfig.getPublicKey();
        Map<String, Object> jwk = new LinkedHashMap<>();
        jwk.put("kty", "RSA");
        jwk.put("use", "sig");
        jwk.put("alg", "RS256");
        jwk.put("kid", jwtConfig.getKeyId());
        jwk.put("n", toBase64UrlUnsigned(publicKey.getModulus()));
        jwk.put("e", toBase64UrlUnsigned(publicKey.getPublicExponent()));

        return ResponseEntity.ok(Map.of("keys", List.of(jwk)));
    }

    private static String toBase64UrlUnsigned(BigInteger bigInt) {
        byte[] bytes = bigInt.toByteArray();
        // Eliminar byte de signo 0x00 inicial si BigInteger lo añadió
        if (bytes.length > 1 && bytes[0] == 0) {
            bytes = Arrays.copyOfRange(bytes, 1, bytes.length);
        }
        return Base64.getUrlEncoder().withoutPadding().encodeToString(bytes);
    }

    @Data
    @NoArgsConstructor
    @AllArgsConstructor
    public static class UserInfo {
        public String id;
        private String username;
        private String password;
        private String role;
        private String tier;
    }

    @Data
    @NoArgsConstructor
    @AllArgsConstructor
    public static class LoginRequest {
        private String username;
        private String password;
    }
}
