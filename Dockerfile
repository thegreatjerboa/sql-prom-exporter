#See https://aka.ms/containerfastmode to understand how Visual Studio uses this Dockerfile to build your images for faster debugging.

FROM mcr.microsoft.com/dotnet/aspnet:5.0 AS base
WORKDIR /app
EXPOSE 80

FROM mcr.microsoft.com/dotnet/sdk:5.0 AS build
WORKDIR /src
COPY ["sql-prom-exporter.csproj", "."]
RUN dotnet restore "./sql-prom-exporter.csproj"
COPY . .
WORKDIR "/src/."
RUN dotnet build "sql-prom-exporter.csproj" -c Release -o /app/build

FROM build AS publish
RUN dotnet publish "sql-prom-exporter.csproj" -c Release -o /app/publish

FROM base AS final
WORKDIR /app
ENV ConnectionStrings:default="Server=tcp:host.docker.internal,1433; Initial Catalog=Insite.Commerce.dogfood; User ID=sa;Password=password;"
COPY --from=publish /app/publish .
ENTRYPOINT ["dotnet", "sql-prom-exporter.dll"]