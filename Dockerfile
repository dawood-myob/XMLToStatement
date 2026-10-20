FROM mcr.microsoft.com/dotnet/sdk:10.0 AS build
WORKDIR /src
COPY src/XMLToStatement/XMLToStatement.csproj src/XMLToStatement/
COPY src/XMLToStatement.Web/XMLToStatement.Web.csproj src/XMLToStatement.Web/
RUN dotnet restore src/XMLToStatement.Web/XMLToStatement.Web.csproj
COPY src/ src/
RUN dotnet publish src/XMLToStatement.Web/XMLToStatement.Web.csproj -c Release -o /app --no-restore

FROM mcr.microsoft.com/dotnet/aspnet:10.0 AS runtime
WORKDIR /app
COPY --from=build /app .
ENV ASPNETCORE_HTTP_PORTS=8080 \
	DOTNET_gcServer=0
EXPOSE 8080
# Numeric UID (1654 = built-in 'app' user) so Kubernetes runAsNonRoot can verify it
USER 1654
ENTRYPOINT ["dotnet", "XMLToStatement.Web.dll"]
