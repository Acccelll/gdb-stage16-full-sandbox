# CrowdRush MVP 0.1.0

Projeto Unity 6 para Android, isolado no subdiretorio `crowdrush/` deste repositorio.

## MVP implementado

- jogo portrait com movimento lateral por toque/arraste;
- crowd logica com visualizacao limitada a 300 unidades;
- gates +, -, x e /;
- inimigos e batalha com reducao gradual da multidao;
- 10 fases deterministicas com dificuldade crescente;
- finish, vitoria, derrota, restart e proxima fase;
- moedas e upgrade de crowd inicial;
- persistencia local via PlayerPrefs;
- configuracao Android: package `com.crowdrush.game`, ARM64, IL2CPP, APK;
- runtime criado apenas com primitivas Unity, sem assets externos obrigatorios.

## Unity Build Automation

Configure o target com:

- Project subfolder path: `crowdrush`
- Platform: Android
- Unity: 6000.x
- Builder: Windows 11 24H2
- Android SDK: 35
- Machine: MICRO / Free tier eligible
- Build App Bundle: desativado (APK)

O script `Assets/Editor/CrowdRushBuildConfig.cs` aplica automaticamente bundle id, portrait, ARM64, IL2CPP e APK no inicio do editor/pre-build.

## Entrada

Cena incluida: `Assets/Scenes/Main.unity`.
O jogo e inicializado automaticamente por `RuntimeInitializeOnLoadMethod`, portanto a cena nao depende de referencias serializadas aos scripts.

## Observacao

Este MVP prioriza um build autocontido e validavel. Arte, audio, VFX e assets comerciais podem ser substituidos posteriormente sem alterar o loop central.
