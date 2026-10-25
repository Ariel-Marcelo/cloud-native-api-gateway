package com.organization.auth;

import lombok.AllArgsConstructor;
import lombok.Data;
import lombok.NoArgsConstructor;
import org.springframework.http.ResponseEntity;
import org.springframework.web.bind.annotation.GetMapping;
import org.springframework.web.bind.annotation.RequestMapping;
import org.springframework.web.bind.annotation.RestController;

import java.net.InetAddress;
import java.net.UnknownHostException;
import java.time.Instant;

@RestController
@RequestMapping("/healthz")
public class HealthController {

    @GetMapping
    public ResponseEntity<HealthInform> healthy() {
        String hostName;
        try {
            hostName =  InetAddress.getLocalHost().getHostName();
        } catch (UnknownHostException e) {
            hostName = "UNKNOWN_HOST";
        }
        var result = new HealthInform("Healthy", "OrdersService", hostName, Instant.now());
        return ResponseEntity.ok(result);
    }

    @Data
    @AllArgsConstructor
    @NoArgsConstructor
    public class HealthInform {
        private String Status;
        private String Service;
        private String Machine;
        private Instant Timestamp = Instant.now();
    }
}
