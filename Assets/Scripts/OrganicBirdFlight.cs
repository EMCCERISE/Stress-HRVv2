using UnityEngine;

public class OrganicBirdFlight : MonoBehaviour
{
    [Header("Velocidade e Rotação")]
    public float speed = 10f;
    public float rotationSpeed = 2f;

    [Header("Ondulação e Vento (Perlin Noise)")]
    public float noiseScale = 0.5f;
    public float noiseFrequency = 0.2f;

    [Header("Proteção do Chão")]
    public float minHeight = 15f;
    public float raycastDistance = 20f;
    public LayerMask groundLayer;

    [Header("Limite de Área (Confinamento)")]
    [Tooltip("Ponto central da cena onde o bando deve orbitar (ex: Stonehenge)")]
    public Transform centerPoint;

    [Tooltip("Distância máxima permitida em relação ao centro")]
    public float maxDistance = 60f;

    [Tooltip("Força com que os pássaros são puxados de volta ao atingir o limite")]
    public float returnForce = 2.5f;

    private float timeOffset;

    void Start()
    {
        timeOffset = Random.Range(0f, 100f);

        // Se nenhum centro for atribuído, usa a origem (0,0,0)
        if (centerPoint == null)
        {
            GameObject origin = new GameObject("BirdCenterPoint");
            origin.transform.position = Vector3.zero;
            centerPoint = origin.transform;
        }
    }

    void Update()
    {
        float time = (Time.time + timeOffset) * noiseFrequency;

        // 1. Gera variação de vento/ruído
        float noiseX = (Mathf.PerlinNoise(time, 0f) - 0.5f) * 2f;
        float noiseY = (Mathf.PerlinNoise(0f, time) - 0.5f) * 2f;
        float noiseZ = (Mathf.PerlinNoise(time, time) - 0.5f) * 2f;

        Vector3 targetDirection = transform.forward + new Vector3(noiseX, noiseY, noiseZ) * noiseScale;

        // 2. RETORNO AO CENTRO (Impede que se afastem demais)
        Vector3 vectorToCenter = centerPoint.position - transform.position;
        float currentDistance = vectorToCenter.magnitude;

        if (currentDistance > maxDistance)
        {
            // Calcula o quanto o bando passou do limite (0 a 1+)
            float distanceFactor = (currentDistance - maxDistance) / maxDistance;

            // Adiciona um vetor suave apontando de volta para o centro
            targetDirection += vectorToCenter.normalized * (distanceFactor * returnForce);
        }

        // 3. EVITA O CHÃO POR ALTITUDE
        if (transform.position.y < minHeight)
        {
            float pushUpForce = (minHeight - transform.position.y) / minHeight;
            targetDirection.y += pushUpForce * 3f;
        }

        // 4. EVITA O CHÃO POR RAYCAST
        RaycastHit hit;
        Vector3 rayDirection = transform.forward + (Vector3.down * 0.5f);

        if (Physics.Raycast(transform.position, rayDirection, out hit, raycastDistance, groundLayer))
        {
            targetDirection += Vector3.up * 5f;
            Debug.DrawLine(transform.position, hit.point, Color.red);
        }

        targetDirection.Normalize();

        // 5. Aplica Rotação e Movimento
        Quaternion targetRotation = Quaternion.LookRotation(targetDirection);
        transform.rotation = Quaternion.Slerp(transform.rotation, targetRotation, Time.deltaTime * rotationSpeed);

        transform.Translate(Vector3.forward * speed * Time.deltaTime);
    }

    void OnDrawGizmosSelected()
    {
        // Desenha uma esfera no Editor para ajudar a visualizar o limite do bando
        Gizmos.color = Color.cyan;
        Vector3 center = centerPoint != null ? centerPoint.position : Vector3.zero;
        Gizmos.DrawWireSphere(center, maxDistance);
    }
}