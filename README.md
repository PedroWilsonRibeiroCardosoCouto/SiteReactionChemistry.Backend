# Reaction Chemistry: Secure Distribution & Gatekeeper API

O **Reaction Chemistry** é um simulador interativo focado no ensino e experimentação. Para garantir a integridade do software e proteger os ativos intelectuais da aplicação (modelos 3D, texturas e código-fonte), foi projetada e implementada uma arquitetura de distribuição segura de ponta a ponta. 

Este repositório contém o código-fonte da **Gatekeeper API**, o microsserviço backend responsável pela orquestração de acessos e pela geração de *downloads* autenticados temporários.

## 🛡️ Arquitetura do Sistema e Prevenção contra Pirataria

O projeto baseia-se num ecossistema de segurança estruturado em três camadas (Client, Data e Cloud), desenhado para mitigar riscos de engenharia reversa e partilha não autorizada de binários:

1. **Proteção de Propriedade Intelectual (Client-side):** 
   O cliente do jogo (Unity) é transpilado nativamente via **IL2CPP (x64)** com níveis elevados de *Managed Stripping*, ofuscando a lógica de negócio. O carregamento de *assets* evita a exposição de diretórios padrão, utilizando o sistema **Addressables** combinado com descriptografia **AES-256 in-memory** em tempo de execução.

2. **Gestão de Identidade e Validação (Data Layer):** 
   A orquestração de permissões é gerida por uma API RESTful em **ASP.NET Core 8**. A camada de persistência utiliza **PostgreSQL** com a abordagem *Code-First Migrations* via **Entity Framework Core**, garantindo a integridade relacional entre as versões do software e os estados de aprovação (`IsActive`).

3. **Infraestrutura de Nuvem e Acesso Temporário (Cloud Layer):** 
   Os binários não são servidos diretamente pela API, poupando largura de banda do servidor. O sistema integra-se com o **Cloudflare R2** (via *Amazon S3 SDK*) para gerar **Presigned URLs**. O algoritmo criptográfico `AWS4-HMAC-SHA256` assina os pedidos de *download*, injetando um **TTL (Time-To-Live) rigoroso de 5 minutos**. Qualquer tentativa de acesso posterior ou manipulação da URL resulta na rejeição do pedido diretamente na CDN.

## 🛠️ Stack Tecnológico

* **Backend:** C# / .NET 8 SDK
* **Persistência:** PostgreSQL (via Docker)
* **ORM:** Entity Framework Core 10
* **Armazenamento em Nuvem:** Cloudflare R2 (S3-Compatible Object Storage)
* **Segurança de Configuração:** .NET Secret Manager (*User Secrets*) para isolamento de credenciais.
* **Documentação de API:** Swagger / OpenAPI

## 🚀 Guia de Implementação e Prova de Conceito (PoC)

As instruções abaixo descrevem a configuração do ambiente de desenvolvimento para replicação desta arquitetura:

**1. Preparação do Ambiente de Dados**
Inicie o serviço de banco de dados e ajuste a *Connection String* no ficheiro `appsettings.json`:
```json
"ConnectionStrings": {
  "DefaultConnection": "Host=localhost;Port=5432;Database=reaction_chemistry_db;Username=admin;Password=sua_senha"
}

""2. Gestão Segura de Credenciais (Cloudflare R2)
Para evitar a exposição de chaves no controlo de versão, utilize o Secret Manager do .NET para injetar as credenciais do Object Storage:
dotnet user-secrets init
dotnet user-secrets set "CloudflareR2:AccessKey" "SUA_ACCESS_KEY"
dotnet user-secrets set "CloudflareR2:SecretKey" "SUA_SECRET_KEY"
dotnet user-secrets set "CloudflareR2:ServiceURL" "SUA_ENDPOINT_URL"

""
**3. Execução das Migrações e Arranque do Servidor**
Gere o esquema relacional no banco de dados e inicie o microsserviço:

"dotnet ef database update
dotnet run"

A interface do Swagger estará disponível para testes dos endpoints em http://localhost:<porta>/swagger.
