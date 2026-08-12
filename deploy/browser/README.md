# Browser container and Kubernetes deployment

Build the static Browser image from the repository root:

```sh
docker build -f Dockerfile.browser -t csv-obfuscator-browser:latest .
docker run --rm -p 8080:8080 csv-obfuscator-browser:latest
```

The image is a two-stage build: the .NET SDK publishes the Avalonia WebAssembly
application with native Skia linking enabled, and nginx serves the resulting
`wwwroot` files. The application runs entirely in the browser; the container
does not process uploaded CSV files on the server.

Install with Helm:

```sh
helm upgrade --install csv-obfuscator ./deploy/browser/helm/csv-obfuscator-browser \
  --set image.repository=registry.example.com/csv-obfuscator-browser \
  --set image.tag=1.0.0
kubectl port-forward svc/csv-obfuscator-csv-obfuscator-browser 8080:80
```

Set `ingress.enabled=true` and configure `ingress.className`, `ingress.hosts`,
and `ingress.tls` for cluster ingress exposure.
