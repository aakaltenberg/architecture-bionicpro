FROM mcr.microsoft.com/dotnet/sdk:8.0 AS build
WORKDIR /src

COPY bionicpro-auth.csproj .
RUN dotnet restore "bionicpro-auth.csproj"

COPY . .

# Публикуем приложение в папку /app
RUN dotnet publish "bionicpro-auth.csproj" -c Release -o /app/publish

FROM mcr.microsoft.com/dotnet/aspnet:8.0 AS runtime
WORKDIR /app
ENV ASPNETCORE_URLS=http://+:8080
EXPOSE 8080

COPY --from=build /app/publish .

ENTRYPOINT ["dotnet", "bionicpro-auth.dll"]