FROM mcr.microsoft.com/dotnet/sdk:8.0 AS build
WORKDIR /src

COPY ["ShortUrl.Api.csproj", "./"]
RUN dotnet restore "ShortUrl.Api.csproj"

COPY . .
RUN dotnet publish "ShortUrl.Api.csproj" -c Release -o /app/publish /p:UseAppHost=false

FROM mcr.microsoft.com/dotnet/aspnet:8.0 AS final
WORKDIR /app

RUN mkdir -p /data && chmod 777 /data

COPY --from=build /app/publish .

ENV ASPNETCORE_URLS=http://+:8080
ENV ConnectionStrings__DefaultConnection="Data Source=/data/shorturl.db"
ENV App__BaseUrl=http://localhost:5000
ENV Security__ApiKey=dev-api-key-secret-9941

EXPOSE 8080
ENTRYPOINT ["dotnet", "ShortUrl.Api.dll"]
