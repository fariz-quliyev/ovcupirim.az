# syntax=docker/dockerfile:1

# OvcuPirim.az frontend — production image: a Vite production build served as static files by
# nginx, which also reverse-proxies /api and /health to the backend and serves /uploads directly
# from the same persistent volume the API writes to (see nginx.conf and docs/deployment.md — this
# is the concrete shape of the "production serves it from the CDN" line in LocalFileStorage's own
# remarks: no CDN has been chosen yet, so nginx serving the mounted volume directly is that role
# for now).
#
# Build context is the `frontend/` directory:
#   docker build -f frontend/Dockerfile -t ovcupirim-web frontend

ARG NODE_VERSION=22

# ---------------------------------------------------------------------------------------------
# Stage: build
# ---------------------------------------------------------------------------------------------
FROM node:${NODE_VERSION}-alpine AS build
WORKDIR /app

COPY package.json package-lock.json ./
RUN npm ci

COPY . .

# Vite inlines VITE_* variables into the built JS at compile time — there is no server-side
# request to inject them into later, so this has to be a build-time ARG, not a container runtime
# environment variable. Left unset by default: the app's own API client already falls back to the
# relative path `/api/v1` (see src/api/client.ts), which is exactly correct when nginx proxies
# /api on the same origin below — the recommended topology, and the one that needs no value here
# at all. Only pass this if the API is deliberately served from a different origin.
ARG VITE_API_BASE_URL=
ENV VITE_API_BASE_URL=${VITE_API_BASE_URL}

RUN npm run build

# ---------------------------------------------------------------------------------------------
# Stage: web — static files plus the reverse proxy in front of them.
# ---------------------------------------------------------------------------------------------
FROM nginx:1.27-alpine AS web

COPY nginx.conf /etc/nginx/conf.d/default.conf
COPY security-headers.conf /etc/nginx/security-headers.conf
COPY --from=build /app/dist /usr/share/nginx/html

EXPOSE 80

# 127.0.0.1, not `localhost`: inside the container `localhost` resolves to ::1 first, nginx.conf
# binds `listen 80;` on IPv4 only, and BusyBox wget — unlike curl — does not fall back to the
# second address. Probing `localhost` therefore fails with "connection refused" on a container
# that is serving every request perfectly, leaving `web` permanently unhealthy. Found on the
# staging host, where the container answered 200 on every route while Docker reported it down.
HEALTHCHECK --interval=30s --timeout=5s --start-period=10s --retries=3 \
    CMD wget --quiet --tries=1 --spider http://127.0.0.1/ || exit 1
