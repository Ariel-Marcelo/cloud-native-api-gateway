package com.organization.auth;

import io.jsonwebtoken.Jwts;
import io.jsonwebtoken.security.Keys;
import jakarta.annotation.PostConstruct;
import lombok.AllArgsConstructor;
import lombok.Data;
import lombok.NoArgsConstructor;
import lombok.extern.slf4j.Slf4j;
import org.springframework.http.HttpStatus;
import org.springframework.http.ResponseEntity;
import org.springframework.web.bind.annotation.*;

import javax.crypto.SecretKey;
import java.nio.charset.StandardCharsets;
import java.sql.Date;
import java.time.Instant;
import java.util.HashMap;
import java.util.Map;

@Slf4j
@RestController
@RequestMapping("/api/authentication")
public class AuthController {

    private final JwtConfig jwtConfig;
    private final Map<String, UserInfo> secretKeyMap = new HashMap<>();

    public AuthController(JwtConfig jwtConfig) {
        this.jwtConfig = jwtConfig;
    }

    @PostConstruct
    public void init() {
        secretKeyMap.put("hash1", new UserInfo("1","Juan", "Admin", "Premium"));
        secretKeyMap.put("hash2", new UserInfo("2","Maria", "Admin", "Premium"));
    }

    @PostMapping
    public ResponseEntity<String> auth(@RequestBody LoginRequest req) {
        log.info("Authentication Init");
        UserInfo userInfo = secretKeyMap.get(req.password);
        if (userInfo == null || userInfo.role.isBlank() || userInfo.tier.isBlank() ) {
            log.error("Authentication Failed '{}' ",req);
            return ResponseEntity.status(HttpStatus.UNAUTHORIZED).build();
        }

        log.info("Generating JWT Token");
        String role = userInfo.role;
        String tier = userInfo.tier;

        String secretKey = jwtConfig.getSecret();
        SecretKey key = Keys.hmacShaKeyFor(secretKey.getBytes(StandardCharsets.UTF_8));
        try {

            return ResponseEntity.ok(Jwts.builder()
                    .subject(userInfo.id)
                    .issuer(jwtConfig.getIssuer())
                    .audience().add(jwtConfig.getAudience()).and()
                    .issuedAt(Date.from(Instant.now()))
                    .expiration(Date.from(Instant.now().plusMillis(jwtConfig.getExpirationMs())))
                    .claim("username", userInfo.username)
                    .claim("role", role)
                    .claim("tier", tier)
                    .signWith(key, Jwts.SIG.HS256).compact());

        } catch (Exception e) {
            log.error("Authentication Failed Exception '{}' ",req, e);
            return ResponseEntity.badRequest().build();
        }
    }

    @Data
    @NoArgsConstructor
    @AllArgsConstructor
    public static class UserInfo {
        public String id;
        private String username;
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
