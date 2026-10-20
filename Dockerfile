# imagen unica de Ventia (la API sirve tambien el frontend compilado)

# etapa 1 (compilar el frontend de Angular)
FROM node:24-alpine AS frontend
WORKDIR /src
COPY frontend/package.json frontend/package-lock.json ./
RUN npm ci
COPY frontend/ ./
RUN npm run build

# etapa 2 (compilar y publicar la API)
FROM mcr.microsoft.com/dotnet/sdk:10.0-alpine AS backend
WORKDIR /src
# primero solo los proyectos (asi la restauracion de paquetes queda en cache si el codigo cambia)
COPY backend/src/Ventia.Domain/Ventia.Domain.csproj Ventia.Domain/
COPY backend/src/Ventia.Application/Ventia.Application.csproj Ventia.Application/
COPY backend/src/Ventia.Infrastructure/Ventia.Infrastructure.csproj Ventia.Infrastructure/
COPY backend/src/Ventia.Api/Ventia.Api.csproj Ventia.Api/
RUN dotnet restore Ventia.Api/Ventia.Api.csproj
COPY backend/src/ ./
RUN dotnet publish Ventia.Api/Ventia.Api.csproj -c Release --no-restore -o /app

# etapa 3 (imagen final solo con lo necesario para ejecutar)
FROM mcr.microsoft.com/dotnet/aspnet:10.0-alpine AS final
# tzdata para las zonas horarias de los negocios (America/Bogota)
RUN apk add --no-cache tzdata
WORKDIR /app
COPY --from=backend /app ./
COPY --from=frontend /src/dist/frontend/browser ./wwwroot

ENV ASPNETCORE_URLS=http://+:8080 \
    ASPNETCORE_ENVIRONMENT=Production
# usuario sin privilegios (viene definido en las imagenes de aspnet)
USER $APP_UID
EXPOSE 8080

HEALTHCHECK --interval=30s --timeout=5s --start-period=40s --retries=3 \
  CMD wget -qO- http://localhost:8080/api/health || exit 1

ENTRYPOINT ["dotnet", "Ventia.Api.dll"]
