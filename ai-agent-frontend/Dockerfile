# Multi-stage build for Angular frontend
FROM node:22-alpine AS build
WORKDIR /app
COPY ai-agent-frontend/package*.json ./
RUN npm ci --legacy-peer-deps
COPY ai-agent-frontend/ .
RUN npm run build -- --configuration production

FROM nginx:alpine AS final
COPY --from=build /app/dist/ai-agent-frontend/browser /usr/share/nginx/html
COPY ai-agent-frontend/nginx.conf /etc/nginx/conf.d/default.conf
EXPOSE 80
CMD ["nginx", "-g", "daemon off;"]
