# Build context is the repo root: docker build -f src/GymStation.Web/Dockerfile .
FROM mcr.microsoft.com/dotnet/sdk:10.0 AS build
ARG VERSION=0.0.0-dev
WORKDIR /src

COPY src/GymStation.Domain/GymStation.Domain.csproj src/GymStation.Domain/
COPY src/GymStation.Infrastructure/GymStation.Infrastructure.csproj src/GymStation.Infrastructure/
COPY src/GymStation.Web/GymStation.Web.csproj src/GymStation.Web/
RUN dotnet restore src/GymStation.Web/GymStation.Web.csproj

COPY src/ src/
RUN dotnet publish src/GymStation.Web/GymStation.Web.csproj -c Release -o /app /p:Version="${VERSION}"

FROM mcr.microsoft.com/dotnet/aspnet:10.0 AS runtime
WORKDIR /app
COPY --from=build /app .
EXPOSE 8080
ENTRYPOINT ["dotnet", "GymStation.Web.dll"]
