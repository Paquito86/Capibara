# Capibara 🦫

**Worker de nodos para el proyecto Iris**

Capibara es un servicio worker diseñado para ser desplegado en cada nodo (servidor) que se desea administrar a través del proyecto Iris. Actúa como un agente local que expone una API REST para la gestión remota de claves SSH y la consulta de logs de backup de MSSQL.

---

## 📋 Tabla de Contenidos

- [Descripción](#descripción)
- [Arquitectura](#arquitectura)
- [Características](#características)
- [Requisitos](#requisitos)
- [Instalación](#instalación)
- [Configuración](#configuración)
- [Endpoints de la API](#endpoints-de-la-api)
- [Despliegue](#despliegue)
- [Futuras Funcionalidades](#futuras-funcionalidades)
- [Licencia](#licencia)

---

## 📖 Descripción

Capibara es un componente esencial del ecosistema Iris. Mientras Iris funciona como el panel central de administración, Capibara debe desplegarse en cada servidor que se quiera gestionar. Este worker:

- **Recibe instrucciones** del panel central Iris
- **Gestiona claves SSH** de forma segura en el servidor local
- **Expone logs de backup** para monitoreo centralizado
- **Proporciona autenticación JWT** para comunicaciones seguras

---

## 🏗️ Arquitectura

```
┌──────────────────┐         ┌──────────────────┐
│                  │         │                  │
│       IRIS       │◄───────►│    Capibara      │
│  (Panel Central) │  HTTPS  │   (Worker Node)  │
│                  │   JWT   │                  │
└──────────────────┘         └──────────────────┘
                                     │
                                     ▼
                             ┌──────────────────┐
                             │  Servidor Local  │
                             │  - SSH Keys      │
                             │  - Logs MSSQL    │
                             └──────────────────┘
```

Cada servidor administrado ejecuta su propia instancia de Capibara, permitiendo a Iris comunicarse de forma segura con cada nodo.

---

## ✨ Características

- **API REST** construida con ASP.NET Core 9
- **Autenticación JWT** con tokens configurables
- **Gestión de claves SSH** con validación de formato
- **Consulta de logs MSSQL** con filtrado por fecha
- **Documentación OpenAPI/Swagger** integrada (en modo desarrollo)
- **Contenedorización Docker** lista para producción
- **Integración con Traefik** para proxy inverso y HTTPS

---

## 📦 Requisitos

- .NET 9.0 SDK (para desarrollo)
- Docker y Docker Compose (para despliegue)
- Red Traefik configurada (para producción con HTTPS)

---

## 🚀 Instalación

### Desarrollo local

```bash
# Clonar el repositorio
git clone https://github.com/Paquito86/Capibara.git
cd Capibara

# Restaurar dependencias y ejecutar
dotnet restore
dotnet run --project Capibara
```

### Docker (desarrollo)

```bash
docker compose -f docker-compose-dev.yml up --build
```

### Docker (producción)

```bash
docker compose -f docker-compose-prod.yml up -d
```

---

## ⚙️ Configuración

La configuración se realiza a través de `appsettings.json` o variables de entorno:

### Configuración JWT

```json
{
  "Jwt": {
    "Issuer": "Capibara",
    "Audience": "CapibaraClients",
    "Key": "tu-clave-secreta-de-al-menos-32-caracteres",
    "ExpireMinutes": 60
  },
  "Auth": {
    "Username": "usuario",
    "Password": "contraseña"
  }
}
```

| Variable | Descripción | Por defecto |
|----------|-------------|-------------|
| `Jwt:Issuer` | Emisor del token JWT | `Capibara` |
| `Jwt:Audience` | Audiencia del token JWT | `CapibaraClients` |
| `Jwt:Key` | Clave secreta para firmar tokens | Requerida |
| `Jwt:ExpireMinutes` | Tiempo de expiración del token (minutos) | `60` |
| `Auth:Username` | Usuario para autenticación | Requerido |
| `Auth:Password` | Contraseña para autenticación | Requerido |

---

## 🔌 Endpoints de la API

### Públicos

| Método | Ruta | Descripción |
|--------|------|-------------|
| `GET` | `/awake/` | Health check - Verifica que el servicio está activo |

### Autenticación

| Método | Ruta | Descripción |
|--------|------|-------------|
| `POST` | `/auth/token` | Obtener token JWT |

**Cuerpo de la petición:**
```json
{
  "username": "usuario",
  "password": "contraseña",
  "expireMinutes": 60
}
```

### Gestión de Claves SSH (requiere autenticación)

| Método | Ruta | Descripción |
|--------|------|-------------|
| `POST` | `/ssh/keys` | Registrar clave pública SSH |

**Cuerpo de la petición (text/plain):**
```
ssh-ed25519 AAAAC3NzaC1lZDI1NTE5AAAAIL7p14I6jkXQeRrB74dcGSG9evn+ItVpmxnhWI77CUc/ mi-clave
```

**Formatos soportados:**
- `ssh-ed25519`
- `ssh-rsa`
- `ecdsa-sha2-nistp256`
- `ecdsa-sha2-nistp384`
- `ecdsa-sha2-nistp521`

### Logs (requiere autenticación)

| Método | Ruta | Descripción |
|--------|------|-------------|
| `GET` | `/logs/mssql?since=FECHA` | Obtener logs de backup MSSQL |

**Parámetros:**
- `since`: Fecha/hora en formato ISO 8601 (ej: `2025-10-18T13:00:00Z`)

---

## 🐳 Despliegue

### Prerrequisitos de Producción

1. Red Docker de Traefik configurada:
```bash
docker network create traefik
```

2. Traefik configurado con certificados SSL (Let's Encrypt)

### Variables de entorno recomendadas

```yaml
environment:
  - ASPNETCORE_URLS=http://+:8080
  - ASPNETCORE_ENVIRONMENT=Production
  - Jwt__Key=tu-clave-secreta-segura
  - Auth__Username=usuario-admin
  - Auth__Password=contraseña-segura
```

---

## 🔮 Futuras Funcionalidades

- [ ] **Gestión de servicios systemd** - Arrancar, detener y reiniciar servicios del sistema
- [ ] **Monitoreo de recursos** - CPU, memoria, disco y red en tiempo real
- [ ] **Ejecución de comandos remotos** - Ejecutar comandos de forma segura desde Iris
- [ ] **Gestión de backups** - Iniciar y programar backups desde el panel central
- [ ] **Actualizaciones automáticas** - Auto-actualización del worker desde Iris
- [ ] **Logs de múltiples servicios** - Soporte para más tipos de logs además de MSSQL
- [ ] **Notificaciones y alertas** - Envío de alertas cuando se detecten problemas
- [ ] **Métricas y estadísticas** - Integración con sistemas de monitoreo (Prometheus, Grafana)
- [ ] **Gestión de usuarios del sistema** - Crear, modificar y eliminar usuarios del servidor
- [ ] **Firewall management** - Control de reglas de firewall desde Iris
- [ ] **Certificados SSL** - Gestión y renovación de certificados
- [ ] **Gestión de cron jobs** - Administración de tareas programadas

---

## 📄 Licencia

Este proyecto está licenciado bajo la **GNU General Public License v3.0**. Ver el archivo [LICENSE.txt](LICENSE.txt) para más detalles.

---

## 🤝 Contribuir

Las contribuciones son bienvenidas. Por favor, abre un issue primero para discutir los cambios que te gustaría hacer.

---

<p align="center">
  <strong>Capibara</strong> - Worker de nodos para Iris 🦫
</p>