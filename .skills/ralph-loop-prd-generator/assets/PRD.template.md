---
task: <nome do objetivo principal>
engine: codex
model: gpt-5
lint_command: <comando de lint>
test_command: <comando de testes>
browser_command: <comando e2e opcional>
---

# PRD: <nome do projeto>

## Contexto

Descrever o cenário, problema e resultado esperado.

## Requisitos

- Requisitos funcionais
- Requisitos não funcionais
- Restrições de stack/arquitetura

## Critérios de Teste

- Escopo de testes unitários
- Escopo de testes integração
- Escopo E2E (se aplicável)

## Critérios de Aceite

- Condições objetivas para considerar entrega concluída

## Fase 1

- [ ] <tarefa atômica 1>
- [ ] <tarefa atômica 2>

## Fase 2

- [ ] <tarefa atômica 3>
- [ ] <tarefa atômica 4>

## Fase 3

- [ ] <tarefa atômica final>

<!-- Exemplo de paralelismo opcional:
- [ ] Criar migration [parallel_group:db]
- [ ] Atualizar repositorio [parallel_group:backend] [depends_on:db]
- [ ] Criar testes unitarios [parallel_group:tests] [depends_on:backend]
-->