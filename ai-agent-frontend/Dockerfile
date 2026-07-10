# Multi-stage build for Angular frontend
FROM node:22-alpine AS build
WORKDIR /app
ARG API_URL=http://localhost:5000
ENV API_URL=$API_URL
COPY package*.json ./
RUN npm ci --legacy-peer-deps
COPY . .

# Replace environment.prod.ts with the actual API URL at build time
RUN echo "export const environment = { production: true, apiUrl: '${API_URL}', signalRUrl: '${API_URL}' };" > src/environments/environment.prod.ts

RUN npm run build -- --configuration production

FROM nginx:alpine AS final
COPY --from=build /app/dist/ai-agent-frontend/browser /usr/share/nginx/html
COPY nginx.conf /etc/nginx/conf.d/default.conf
EXPOSE 80
CMD ["nginx", "-g", "daemon off;"]
