# Project Context — APCS

## What is APCS?

APCS (AI-Powered POD Content Studio) is a graduation project that provides an AI-powered platform for Print-on-Demand content creation and management.

## Backend Responsibility

The backend provides REST APIs for authentication, user management, and future business features. It is built with ASP.NET Core 8 using Clean Architecture with MediatR for CQRS.

## Current Phase

**Foundation** — project structure, Clean Architecture layers, JWT authentication, and core infrastructure are in place. No business domain features are implemented yet.

## Current Implemented Foundation

- Clean Architecture (API, Application, Domain, Infrastructure, Common)
- JWT authentication with access + refresh tokens
- ASP.NET Core Identity with PostgreSQL
- MediatR CQRS with FluentValidation pipeline
- Global exception handling middleware
- Result/Error pattern for operation outcomes
- CORS, Swagger/OpenAPI configuration

## Next Likely Backend Modules

These are planned but **not yet implemented**:

- **API Key Management** — external service authentication
- **Batch Input** — CSV/Excel upload processing
- **Product Queue** — queued product processing
- **Redis** — distributed caching
- **Hangfire** — background job processing
