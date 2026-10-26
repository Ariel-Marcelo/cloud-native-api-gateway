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
import java.time.Instant;
import java.util.Date;
import java.util.HashMap;
import java.util.Map;

@Slf4j
@RestController
@RequestMapping("/api/authentication")
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

        log.info("Generating JWT Token for user '{}'", userInfo.getUsername());
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
            log.error("Authentication Failed Exception for user '{}'", req.getUsername(), e);
            return ResponseEntity.badRequest().build();
        }
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
