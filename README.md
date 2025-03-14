# ClientCertEcho

## Server

Start the server on `https://localhost:5001`:

```
cd ClientCertEchoServer
dotnet run -c release
```

## Client

### Available strategies

- Keyed Singleton (`Singleton`):
    - HttpClients are registered as AnyKey keyed singletons
    - Same configuration method executes for all clients
    - No HttpClientFactory
    - Singletons are not cleaned up after use
- Lazy Init (`LazyInit`)
    - Certificate callback is init on the first request of the handler
    - Context is passed via request options
    - Exactly same config for all clients beyond the lazily init client certs (ConfigureHttpClientDefaults doesn't provide the name to he callback)
    - First request is much slower
- ConfigureOptions (`ConfigureOptions`)
    - An alternative to Lazy Init
    - Directly edits HttpClientFactoryOptions and HttpMessageHandler builder which is not recommended
    - More configuration freedom as technically almost all config can be done via ConfigureOptions, though I'd consider it an implementation detail
- Multi-Handler (`Multi-Handler`)
    - Move the cert switch to the inside of the handler chain
    - Can have as many different named configs as you like, since the same handler chain is used for all the certs
    - Need to implement manual caching

### Strategy select

Start the client with a default strategy (the one specified in the `.csproj`, in the tag `<DefaultStrategy>`)

```
dotnet run -c release
```

-OR-

Start the client with a specific strategy, e.g.:

```
dotnet run -c release /p:Strategy=MultiHandler
```
