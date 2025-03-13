# ClientCertEcho

## Run server

Start the server on `https://localhost:5001`:

```
cd ClientCertEchoServer
dotnet run -c release
```

## Run client

Available strategies:

- Keyed Singleton (`Singleton`)
- HttpClientDefaults (`Defaults`)
- Multi-Handler (`Multi-Handler`)

Start the client with a default strategy (the one specified in the `.csproj`, in the tag `<DefaultStrategy>`)

```
dotnet run -c release
```

-OR-

Start the client with a specific strategy, e.g.:

```
dotnet run -c release /p:Strategy=MultiHandler
```
