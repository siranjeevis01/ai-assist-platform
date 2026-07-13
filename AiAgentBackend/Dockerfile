# Stage 1: Build Angular frontend
FROM node:20-alpine AS frontend-build
WORKDIR /frontend
COPY ai-agent-frontend/package.json ai-agent-frontend/package-lock.json* ./
RUN npm ci --ignore-scripts
COPY ai-agent-frontend/ .
RUN npm run build -- --configuration=production

# Stage 2: Build .NET backend
FROM mcr.microsoft.com/dotnet/sdk:9.0 AS build
WORKDIR /src
COPY ["AiAgentBackend/AiAgentBackend.csproj", "AiAgentBackend/"]
RUN dotnet restore "AiAgentBackend/AiAgentBackend.csproj"
COPY AiAgentBackend/ AiAgentBackend/
RUN dotnet publish "AiAgentBackend/AiAgentBackend.csproj" -c Release -o /app/publish

# Stage 3: Final image
FROM mcr.microsoft.com/dotnet/aspnet:9.0 AS final
WORKDIR /app
COPY --from=build /app/publish .
# Copy Angular build output into wwwroot for static file serving
COPY --from=frontend-build /frontend/dist/ai-agent-frontend/browser/ ./wwwroot/
ENV ASPNETCORE_ENVIRONMENT=Production
ENV ASPNETCORE_URLS=http://*:5000
EXPOSE 5000
ENTRYPOINT ["dotnet", "AiAgentBackend.dll"]
