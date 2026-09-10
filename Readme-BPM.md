# Solução de Coleta de Dados de BPM

Esta solução permite a coleta de dados de frequência cardíaca (BPM) de um monitor cardíaco Bluetooth (como o Coospo HW9) a partir de um PC, e o envio em tempo real para uma aplicação Unity na mesma rede. Os dados recebidos são utilizados para controlar de forma dinâmica elementos do jogo, como a intensidade das luzes do ambiente.

## Estrutura da Solução

A solução é composta pelos seguintes arquivos principais:

### Lado do Servidor (PC)
* **`Assets/Scripts/main.py`**: Script em Python que se conecta ao monitor cardíaco via Bluetooth Low Energy (BLE). Ele faz a leitura da frequência cardíaca e expõe esses dados via uma API HTTP local (FastAPI). Além disso, ele envia "beacons" (anúncios) UDP na rede (porta 5001) para que os clientes (Unity) descubram seu IP e porta automaticamente, sem precisar de configuração manual.

### Lado do Cliente (Unity)
Todos os scripts Unity ficam na pasta `Assets/Scripts/`:
* **`LanDiscovery.cs`**: Fica escutando a rede em busca dos anúncios UDP enviados pelo `main.py`. Assim que ele encontra o servidor Python, extrai o endereço IP e a porta da API, repassando-os para o `BpmClient.cs`.
* **`BpmClient.cs`**: Gerencia a conexão com a API do PC. Ele faz requisições HTTP (`/bpm`) de forma periódica para buscar o valor mais recente do BPM e dispara um evento (C# `event`) sempre que a frequência cardíaca muda. Possui também lógica de *fallback* configurável (valor padrão) para o caso de desconexões.
* **`HeartRateLightController.cs`**: Subscreve ao evento do `BpmClient.cs`. Com base na frequência cardíaca reportada, ele suaviza e interpola a intensidade de diversas luzes designadas, alterando o ambiente visual da cena para refletir o estado medido (uma luz relaxada vs. mais agitada).

### Cena de Exemplo
* **`Assets/Scenes/SampleScene.unity`**: Cena pronta que demonstra a integração de todos esses scripts funcionando em conjunto. Nela os GameObjects já estão com as referências dos scripts interligadas.

## Pré-requisitos (Python)

Para rodar o script no seu computador, é necessário ter o Python instalado e instalar as dependências associadas. Abra um terminal na pasta `Assets/Scripts/` (ou onde o `main.py` estiver) e execute:

```bash
pip install fastapi uvicorn[standard] bleak
```

> **Atenção**: O script foi configurado para buscar inicialmente o dispositivo pelo MAC Address `EA:30:3E:50:BF:21`. Caso seu dispositivo tenha um MAC Address diferente ou mude a cada conexão, o script tentará fazer um escaneamento pelo nome (`COOSPO`, `HW9`). Se necessário, edite o arquivo `main.py` para atualizar a constante `DEVICE_ADDRESS`.

## Como Executar

### 1. Iniciar o Servidor no PC
Certifique-se de que o monitor cardíaco está ligado e próximo ao PC (normalmente eles requerem estar vestidos no peito, com eletrodos levemente úmidos, para iniciar a transmissão). Em seguida, execute o servidor via uvicorn:

```bash
cd Assets/Scripts/
python -m uvicorn main:app --host 0.0.0.0 --port 5000
```
O console deverá exibir mensagens informando que se conectou ao BLE e que começou a enviar pacotes de beacon via rede.

### 2. Iniciar a cena na Unity
1. Abra o projeto na Unity.
2. Carregue a cena `SampleScene` localizada em `Assets/Scenes/`.
3. Dê **Play** no editor.
4. O script `LanDiscovery` deve identificar automaticamente o servidor rodando no seu computador e vai conectar o `BpmClient`.
5. Se tudo correr bem, você poderá observar os valores de BPM sendo atualizados e a iluminação (assim como qualquer outro script atrelado) reagindo instantaneamente.

---

## Estratégia de Biofeedback Visual e Fisiológico

Para maximizar a imersão e induzir relaxamento de forma clinicamente embasada, o projeto utiliza uma estratégia visual guiada por **Biofeedback** (via BPM e HRV) e **Psicologia das Cores**, agindo diretamente no Sistema Nervoso Autônomo do usuário.

### 1. BPM vs. HRV (Variabilidade da Frequência Cardíaca)
Enquanto a Frequência Cardíaca (BPM) mede a "velocidade média" do coração, a Variabilidade da Frequência Cardíaca (HRV) mede a microvariação de tempo em milissegundos *entre cada batida consecutiva* (intervalos R-R). A integração nativa do Quest suporta a extração e cálculo de **RMSSD** (Root Mean Square of Successive Differences), a métrica padrão-ouro para medir o tônus vagal em tempo real.
* **Alto HRV / RMSSD (Relaxamento):** O coração responde elasticamente à respiração (acelera ao inspirar, desacelera ao expirar). Indica dominância do **Sistema Nervoso Parassimpático** ("descanso e digestão"). É o estado de calma, resiliência e coerência cardíaca.
* **Baixo HRV / RMSSD (Estresse):** O coração bate de forma rígida, como um metrônomo. Indica dominância do **Sistema Nervoso Simpático** ("luta ou fuga"). O usuário está tenso ou em alerta.

### 2. Sincronização Fisiológica (Entrainment)
A velocidade de simulação do ambiente virtual (como a movimentação de partículas) é diretamente atrelada aos sinais cardíacos. Conforme o usuário diminui o BPM ou aumenta o HRV através da respiração lenta, o ambiente desacelera. O cérebro humano tem a tendência natural de espelhar ritmos externos, então ver o ambiente "acalmando" cria um ciclo de feedback positivo que aprofunda o relaxamento.

### 3. Psicologia das Cores
* Conforme o estado fisiológico atinge zonas de relaxamento (alta coerência), o sistema transita suavemente para tons pastéis de **azul, ciano e verde-água**. Estudos demonstram que comprimentos de onda curtos ativam efetivamente o sistema nervoso parassimpático.
* Em estados de alerta (BPM alto ou HRV baixo), as cores migram para tons mais quentes e estimulantes, indicando subconscientemente ao usuário a necessidade de regular a respiração.

### 4. Pacing Respiratório e Expansão Espacial
Nos estados mais calmos, a área de emissão dos efeitos visuais se expande sutilmente, enquanto as partículas crescem e diminuem lentamente. A sensação de "expansão do espaço" reduz instintos de claustrofobia. Além disso, observar um objeto expandindo e contraindo suavemente ajuda a guiar o ritmo respiratório do usuário para a taxa ideal de coerência (cerca de 6 respirações por minuto).

### 5. Turbulência e Fluxo Laminar (Noise)
Na natureza, movimentos caóticos transmitem perigo e instabilidade instintiva (como enxames ou fumaça de incêndio). No auge do relaxamento, o script zera quase completamente a turbulência do sistema de partículas, resultando em um fluxo "laminar" e previsível que transmite uma forte sensação de estabilidade e segurança ao cérebro subconsciente.
