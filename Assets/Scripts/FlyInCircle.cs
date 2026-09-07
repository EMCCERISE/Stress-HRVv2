using UnityEngine;

public class FlyInCircle : MonoBehaviour
{
    [Header("Configurações do Círculo")]
    [Tooltip("Raio do círculo em unidades da Unity")]
    public float radius = 5f;

    [Tooltip("Velocidade do voo (angular)")]
    public float speed = 2f;

    [Header("Eixo do Movimento")]
    [Tooltip("Marque se o jogo for 2D. Desmarque se for 3D.")]
    public bool is2D = false;

    private float angle;
    private Vector3 centerPosition;

    void Start()
    {
        // Define o centro do círculo como a posição inicial do pássaro
        centerPosition = transform.position;
    }

    void Update()
    {
        // Incrementa o ângulo com base no tempo e velocidade
        angle += speed * Time.deltaTime;

        // Posição no frame anterior para calcular o vetor de direção
        Vector3 previousPosition = transform.position;

        // 1. CALCULA A NOVA POSIÇÃO
        float x = Mathf.Cos(angle) * radius;
        float yZ = Mathf.Sin(angle) * radius; // Serve para Y (2D) ou Z (3D)

        Vector3 newPosition;
        if (is2D)
        {
            // Movimento no plano 2D (XY)
            newPosition = centerPosition + new Vector3(x, yZ, 0f);
        }
        else
        {
            // Movimento no plano 3D (XZ)
            newPosition = centerPosition + new Vector3(x, 0f, yZ);
        }

        transform.position = newPosition;

        // 2. ORIENTA O PÁSSARO NA DIREÇÃO DO MOVIMENTO
        Vector3 moveDirection = (newPosition - previousPosition).normalized;

        if (moveDirection != Vector3.zero)
        {
            if (is2D)
            {
                // Para 2D: calcula o ângulo Z em graus a partir da direção
                float angleZ = Mathf.Atan2(moveDirection.y, moveDirection.x) * Mathf.Rad2Deg;
                transform.rotation = Quaternion.Euler(0f, 0f, angleZ);
            }
            else
            {
                // Para 3D: faz a frente da mesh (eixo Z positivo) olhar na direção do vetor
                transform.rotation = Quaternion.LookRotation(moveDirection);
            }
        }
    }
}
