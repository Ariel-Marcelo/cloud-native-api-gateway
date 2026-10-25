package com.organization.auth;

import org.springframework.beans.factory.annotation.Value;
import org.springframework.stereotype.Component;

@Component
public class JwtConfig {

    @Value("${jwt.secret}")
    private String secret;

    @Value("${jwt.issuer}")
    private String issuer;

    @Value("${jwt.audience}")
    private String audience;

    @Value("${jwt.expiration-ms}")
    private Long expirationMs;

    public String getSecret() {
        return secret;
    }

    public String getIssuer() {
        return issuer;
    }

    public Long getExpirationMs() {
        return expirationMs;
    }

    public String getAudience() {
        return audience;
    }
}
