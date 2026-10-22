FROM maven:3.10.0-eclipse-temurin-17 AS build
WORKDIR /source

COPY ["src/Services/OrdersService/pom.xml", "pom.xml"]
RUN mvn dependency:go-offline -B

COPY ["src/Services/OrdersService/src", "src"]
RUN mvn clean package -DskipTests


FROM eclipse-temurin:17-jre-noble AS final
WORKDIR /source
EXPOSE 8083
ENV PORT=8083
COPY --from=build /source/target/*.jar app.jar

ENTRYPOINT [ "java", "-jar", "app.jar" ]

