FROM maven:3.10.0-eclipse-temurin-17 AS build
WORKDIR /source

COPY ["src/Services/AuthService/pom.xml", "pom.xml"]
RUN mvn dependency:go-offline -B

COPY ["src/Services/AuthService/src", "src"]
RUN mvn clean package -DskipTests

FROM eclipse-temurin:17-jre-noble AS final
WORKDIR /source
EXPOSE 8085
ENV PORT=8085
COPY --from=build /source/target/*.jar app.jar

ENTRYPOINT [ "java", "-jar", "app.jar" ]