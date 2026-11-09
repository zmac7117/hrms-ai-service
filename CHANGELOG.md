# Changelog

## Unreleased
- Invoice parsing with OCR (module `invoice`), shared document reading and OCR project (`HrmsAi.Documents`).
- Control panel v3: publish selector for any host build, release switch, tools table, model download, bug fix for starting and stopping Ollama and the tunnel.

## Client dashboard
- Dashboard for clients (`/dashboard`) and admin activity page (`/admin/activity`); history of direct AI calls.

## AI feature modules
- Job description generator and goals generator (Groq-compatible and native routes), resume-to-job match score (SharpAPI-compatible), shared AI slots so jobs and direct calls never overload the CPU, Bearer key support, `new-host.ps1` for per-client builds.

## Modular platform
- Solution split into modules and hosts; API keys with token limits and an admin page; fair job queue with limits and estimates; `publish.ps1` with rollback.

## First version
- Resume parsing (PDF, DOCX) with a fixed JSON contract, Ollama as a Windows service, IIS hosting, public access through a tunnel.