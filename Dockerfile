FROM mcr.microsoft.com/dotnet/sdk:9.0 AS build
WORKDIR /src
COPY global.json Directory.Build.props Directory.Packages.props ./
COPY src/WalletApi.Domain/*.csproj src/WalletApi.Domain/
COPY src/WalletApi.Application/*.csproj src/WalletApi.Application/
COPY src/WalletApi.Infrastructure/*.csproj src/WalletApi.Infrastructure/
COPY src/WalletApi.Api/*.csproj src/WalletApi.Api/
RUN dotnet restore src/WalletApi.Api
COPY src src
RUN dotnet publish src/WalletApi.Api -c Release -o /out --no-restore

FROM mcr.microsoft.com/dotnet/aspnet:9.0
WORKDIR /app
COPY --from=build /out .
USER $APP_UID
ENV ASPNETCORE_URLS=http://+:8080
ENTRYPOINT ["dotnet", "WalletApi.Api.dll"]
