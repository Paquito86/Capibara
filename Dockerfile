# Esta fase se usa cuando se ejecuta desde VS en modo rápido (valor predeterminado para la configuración de depuración)
FROM mcr.microsoft.com/dotnet/aspnet:9.0 AS base
# Configurar UTF-8 como encoding por defecto
ENV LANG=C.UTF-8
ENV LC_ALL=C.UTF-8
WORKDIR /app
EXPOSE 8080
EXPOSE 8081

# Esta fase se usa para compilar el proyecto de servicio
FROM mcr.microsoft.com/dotnet/sdk:9.0 AS build
# Configurar UTF-8 también en el contenedor de build
ENV LANG=C.UTF-8
ENV LC_ALL=C.UTF-8
ARG BUILD_CONFIGURATION=Release
WORKDIR /src
COPY ["Capibara/Capibara.csproj", "Capibara/"]
RUN dotnet restore "./Capibara/Capibara.csproj"
COPY . .
WORKDIR "/src/Capibara"
RUN dotnet build "./Capibara.csproj" -c $BUILD_CONFIGURATION -o /app/build

# Esta fase se usa para publicar el proyecto de servicio que se copiará en la fase final.
FROM build AS publish
ARG BUILD_CONFIGURATION=Release
RUN dotnet publish "./Capibara.csproj" -c $BUILD_CONFIGURATION -o /app/publish /p:UseAppHost=false

# Esta fase se usa en producción o cuando se ejecuta desde VS en modo normal (valor predeterminado cuando no se usa la configuración de depuración)
FROM base AS final
WORKDIR /app
COPY --from=publish /app/publish .
ENTRYPOINT ["dotnet", "Capibara.dll"]
