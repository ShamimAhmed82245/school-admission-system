# School Admission System

A web-based school admission system with a first-come, first-served seat reservation process. It allows administrators to create classes and manage seat capacity, while students can apply, hold seats temporarily, confirm admission, and track their applications.

## Technology Stack

- **Backend:** C# / ASP.NET Core (.NET 10)
- **Frontend:** React / TypeScript
- **Database:** PostgreSQL
- **ORM:** Entity Framework Core
- **Containerization:** Docker & Docker Compose

## Project Structure

```text
school-admission-system/
├── backend/
│   ├── SchoolAdmission.Api/
│   └── SchoolAdmission.Api.Tests/
├── frontend/
├── docker-compose.yml
├── .env.example
├── ASSUMPTIONS.md
├── AI_USAGE.md
└── README.md
```

## Prerequisites

- Docker Desktop
- Docker Compose

No separate installation of .NET SDK, Node.js, or PostgreSQL is required.

## Environment Configuration

Clone the repository:

```bash
git clone https://github.com/ShamimAhmed82245/school-admission-system.git

cd school-admission-system
```

Create a `.env` file in the project root using `.env.example` as a reference.

Configure the following variables:

```dotenv
POSTGRES_DB=
POSTGRES_USER=
POSTGRES_PASSWORD=

HOLD_DURATION_SECONDS=600

VITE_USE_BACKEND_API=true
VITE_API_BASE_URL=http://localhost:8080
```

## Run the Application

Build and start all services:

```bash
docker compose up --build -d
```

This starts PostgreSQL, the backend API, and the frontend.

**Frontend URL:** http://localhost:3000

**Backend URL:** http://localhost:8080

To check running containers:

```bash
docker compose ps
```

To view application logs:

```bash
docker compose logs -f
```

To stop the application:

```bash
docker compose down
```

PostgreSQL data is persisted using a Docker named volume.

## Run Tests

The backend test project can be executed using the .NET SDK Docker image, without installing the SDK locally.

Run this command from the project root in PowerShell:

```powershell
docker run --rm --mount "type=bind,source=$($PWD.Path)\backend,target=/src" -w /src mcr.microsoft.com/dotnet/sdk:10.0-alpine dotnet test SchoolAdmission.Api.Tests/SchoolAdmission.Api.Tests.csproj
```

## Frontend Data Source

The frontend supports mock data and backend API integration through environment variables.

- `VITE_USE_BACKEND_API=false` — Uses mock data.
- `VITE_USE_BACKEND_API=true` — Uses the backend API.
- `VITE_API_BASE_URL` — Configures the backend API base URL.

After changing frontend environment variables, rebuild the frontend:

```bash
docker compose up --build -d
```

## Repository

[GitHub Repository](https://github.com/ShamimAhmed82245/school-admission-system)