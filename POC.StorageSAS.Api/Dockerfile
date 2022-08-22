#See https://aka.ms/containerfastmode to understand how Visual Studio uses this Dockerfile to build your images for faster debugging.

FROM mcr.microsoft.com/dotnet/aspnet:6.0 AS base
WORKDIR /app
EXPOSE 80
EXPOSE 443

FROM mcr.microsoft.com/dotnet/sdk:6.0 AS build
WORKDIR /src
COPY ["POC.StorageSAS.Api/POC.StorageSAS.Api.csproj", "POC.StorageSAS.Api/"]
RUN dotnet restore "POC.StorageSAS.Api/POC.StorageSAS.Api.csproj"
COPY . .
WORKDIR "/src/POC.StorageSAS.Api"
RUN dotnet build "POC.StorageSAS.Api.csproj" -c Release -o /app/build

FROM build AS publish
RUN dotnet publish "POC.StorageSAS.Api.csproj" -c Release -o /app/publish

FROM base AS final
WORKDIR /app
COPY --from=publish /app/publish .
ENTRYPOINT ["dotnet", "POC.StorageSAS.Api.dll"]