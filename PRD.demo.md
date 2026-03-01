---
task: Demo Ralph Loop
engine: codex
model: gpt-5
lint_command: dotnet format --verify-no-changes
test_command: dotnet test
browser_command: npx playwright test
---

# PRD Demo

Use este arquivo como exemplo para testar `run` e `parallel`.

## Fase 1

- [ ] Criar migration de banco [parallel_group:db]
- [ ] Atualizar repositórios de persistência [parallel_group:backend] [depends_on:db]
- [ ] Implementar endpoint GET /health [parallel_group:backend]

## Fase 2

- [ ] Criar testes unitários da camada de serviço [parallel_group:tests] [depends_on:backend]
- [ ] Criar testes de integração da API [parallel_group:tests] [depends_on:backend]
- [ ] Ajustar documentação do endpoint [parallel_group:docs] [depends_on:backend]

## Fase 3

- [ ] Validar fluxo E2E no navegador [parallel_group:e2e] [depends_on:tests]
- [ ] Gerar changelog da entrega [parallel_group:release] [depends_on:e2e]

