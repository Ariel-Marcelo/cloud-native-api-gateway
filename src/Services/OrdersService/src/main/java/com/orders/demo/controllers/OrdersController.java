package com.orders.demo.controllers;

import jakarta.annotation.PostConstruct;
import lombok.AllArgsConstructor;
import lombok.Data;
import lombok.NoArgsConstructor;
import lombok.extern.slf4j.Slf4j;
import org.springframework.http.ResponseEntity;
import org.springframework.web.bind.annotation.GetMapping;
import org.springframework.web.bind.annotation.RequestMapping;
import org.springframework.web.bind.annotation.RestController;

import java.time.Instant;
import java.util.ArrayList;
import java.util.List;
import java.util.UUID;

@Slf4j
@RestController
@RequestMapping("/api/orders")
public class OrdersController {

    private final List<Order> ordersSaved = new ArrayList<>();


    @PostConstruct
    public void init() {
        ordersSaved.add(new Order(List.of(UUID.randomUUID()), Instant.now()));
        ordersSaved.add(new Order(List.of(UUID.randomUUID(), UUID.randomUUID()), Instant.now()));
    }

    @GetMapping
    public ResponseEntity<List<Order>> getAllOrders() {
        log.info("Consultando usuarios");
        return ResponseEntity.ok(ordersSaved);
    }

    @Data
    @AllArgsConstructor
    @NoArgsConstructor
    public class Order {

        public List<UUID> ProductsIds;

        public Instant OrderDate = Instant.now();
    }
}
