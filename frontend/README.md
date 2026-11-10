# School Admission System Frontend

React, TypeScript, Vite, and Tailwind CSS frontend for the school admission assignment.

## Service modes

Copy `.env.example` to `.env` and choose one mode:

```env
VITE_USE_BACKEND_API=false
VITE_API_BASE_URL=http://localhost:8080
```

- `VITE_USE_BACKEND_API=false` uses the localStorage-backed mock service and makes no backend requests.
- `VITE_USE_BACKEND_API=true` uses the backend exclusively for classes, holds, seat counts, and applications.

Run `pnpm build` to verify a production build.

## Docker

Vite environment variables are compiled into the frontend bundle at build time. Changing either value requires rebuilding the frontend image.

```sh
docker build \
  --build-arg VITE_USE_BACKEND_API=true \
  --build-arg VITE_API_BASE_URL=http://localhost:8080 \
  -t school-admission-frontend .
```

If this frontend is used from a root `docker-compose.yml`, pass the same values under the frontend service's `build.args`.
