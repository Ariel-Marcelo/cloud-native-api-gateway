package com.organization.auth;

import jakarta.annotation.PostConstruct;
import org.springframework.beans.factory.annotation.Value;
import org.springframework.stereotype.Component;

import java.security.KeyPair;
import java.security.KeyPairGenerator;
import java.security.NoSuchAlgorithmException;
import java.security.PrivateKey;
import java.security.interfaces.RSAPublicKey;
import java.util.UUID;

@Component
public class JwtConfig {

    @Value("${jwt.issuer}")
    private String issuer;

    @Value("${jwt.audience}")
    private String audience;

    @Value("${jwt.expiration-ms}")
    private Long expirationMs;

    private KeyPair keyPair;
    private String keyId;

    @PostConstruct
    public void initKeys() {
        try {
            KeyPairGenerator keyPairGenerator = KeyPairGenerator.getInstance("RSA");
            keyPairGenerator.initialize(2048);
            this.keyPair = keyPairGenerator.generateKeyPair();
            this.keyId = "auth-rs256-" + UUID.randomUUID().toString().substring(0, 8);
        } catch (NoSuchAlgorithmException e) {
            throw new IllegalStateException("No se pudo inicializar el par de claves RSA para RS256", e);
        }
    }

    public PrivateKey getPrivateKey() {
        return keyPair.getPrivate();
    }

    public RSAPublicKey getPublicKey() {
        return (RSAPublicKey) keyPair.getPublic();
    }

    public String getKeyId() {
        return keyId;
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
