FROM mcr.microsoft.com/dotnet/sdk:10.0 AS build
WORKDIR /src

COPY Whispr_RealtimeService.sln ./
COPY Application/Application.csproj Application/
COPY Contracts/Contracts.csproj Contracts/
COPY Domain/Domain.csproj Domain/
COPY Infrastructure.Grpc/Infrastructure.Grpc.csproj Infrastructure.Grpc/
COPY Infrastructure.Messaging/Infrastructure.Messaging.csproj Infrastructure.Messaging/
COPY Infrastructure.Redis/Infrastructure.Redis.csproj Infrastructure.Redis/
COPY Services/Services.csproj Services/

RUN dotnet restore Services/Services.csproj

COPY . .
RUN dotnet publish Services/Services.csproj -c Release -o /app/publish /p:UseAppHost=false

FROM mcr.microsoft.com/dotnet/aspnet:10.0 AS runtime
WORKDIR /app

RUN groupadd --system appgroup \
    && useradd --system --gid appgroup --uid 10001 appuser

ENV ASPNETCORE_URLS=http://+:8080
ENV DOTNET_EnableDiagnostics=0

COPY --from=build /app/publish ./

EXPOSE 8080
USER appuser

ENTRYPOINT ["dotnet", "Services.dll"]
