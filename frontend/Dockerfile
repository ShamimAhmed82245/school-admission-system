FROM node:22-alpine AS build

RUN corepack enable
WORKDIR /app

COPY package.json pnpm-lock.yaml ./
RUN pnpm install --frozen-lockfile

COPY . .

ARG VITE_USE_BACKEND_API=false
ARG VITE_API_BASE_URL=http://localhost:8080
ENV VITE_USE_BACKEND_API=${VITE_USE_BACKEND_API}
ENV VITE_API_BASE_URL=${VITE_API_BASE_URL}

RUN pnpm build

FROM nginx:1.27-alpine

COPY nginx.conf /etc/nginx/conf.d/default.conf
COPY --from=build /app/dist /usr/share/nginx/html

EXPOSE 80

CMD ["nginx", "-g", "daemon off;"]
