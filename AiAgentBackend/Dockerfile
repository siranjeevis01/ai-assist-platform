# Multi-stage build for production
FROM mcr.microsoft.com/dotnet/aspnet:9.0 AS base
WORKDIR /app
EXPOSE 5000
EXPOSE 5001

FROM mcr.microsoft.com/dotnet/sdk:9.0 AS build
WORKDIR /src
COPY ["AiAgentBackend/AiAgentBackend.csproj", "."]
RUN dotnet restore "AiAgentBackend.csproj"
COPY AiAgentBackend/ .
RUN dotnet publish "AiAgentBackend.csproj" -c Release -o /app/publish

FROM base AS final
WORKDIR /app
COPY --from=build /app/publish .
ENV ASPNETCORE_ENVIRONMENT=Production
ENV ASPNETCORE_URLS=http://*:5000
ENTRYPOINT ["dotnet", "AiAgentBackend.dll"]
