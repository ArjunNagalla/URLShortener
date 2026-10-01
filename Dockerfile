FROM mcr.microsoft.com/dotnet/sdk:8.0 AS build
WORKDIR /src
COPY ["src/ShortUrl.Api/ShortUrl.Api.csproj", "src/ShortUrl.Api/"]
RUN dotnet restore "src/ShortUrl.Api/ShortUrl.Api.csproj"
COPY . .
WORKDIR "/src/src/ShortUrl.Api"
RUN dotnet build "ShortUrl.Api.csproj" -c Release -o /app/build
RUN dotnet publish "ShortUrl.Api.csproj" -c Release -o /app/publish /p:UseAppHost=false

FROM mcr.microsoft.com/dotnet/aspnet:8.0 AS final
WORKDIR /app
COPY --from=build /app/publish .
ENV ASPNETCORE_URLS=http://+:8080
EXPOSE 8080
ENTRYPOINT ["dotnet", "ShortUrl.Api.dll"]
