FROM mcr.microsoft.com/dotnet/sdk:10.0 AS build

WORKDIR /src

COPY SchoolAdmission.Api/SchoolAdmission.Api.csproj SchoolAdmission.Api/

RUN dotnet restore SchoolAdmission.Api/SchoolAdmission.Api.csproj

COPY . .

RUN dotnet publish SchoolAdmission.Api/SchoolAdmission.Api.csproj \
    -c Release \
    -o /app/publish \
    /p:UseAppHost=false

FROM mcr.microsoft.com/dotnet/aspnet:10.0 AS final

WORKDIR /app

COPY --from=build /app/publish .

ENV ASPNETCORE_URLS=http://+:8080

EXPOSE 8080

ENTRYPOINT ["dotnet", "SchoolAdmission.Api.dll"]