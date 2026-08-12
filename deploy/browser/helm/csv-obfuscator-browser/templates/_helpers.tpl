{{- define "csv-obfuscator-browser.name" -}}
{{- default .Chart.Name .Values.nameOverride | trunc 63 | trimSuffix "-" }}
{{- end }}
{{- define "csv-obfuscator-browser.fullname" -}}
{{- if .Values.fullnameOverride }}{{ .Values.fullnameOverride | trunc 63 | trimSuffix "-" }}{{ else }}{{ printf "%s-%s" .Release.Name (include "csv-obfuscator-browser.name" .) | trunc 63 | trimSuffix "-" }}{{ end }}
{{- end }}
{{- define "csv-obfuscator-browser.labels" -}}
app.kubernetes.io/name: {{ include "csv-obfuscator-browser.name" . }}
app.kubernetes.io/instance: {{ .Release.Name }}
app.kubernetes.io/version: {{ .Chart.AppVersion | quote }}
app.kubernetes.io/managed-by: {{ .Release.Service }}
{{- end }}
