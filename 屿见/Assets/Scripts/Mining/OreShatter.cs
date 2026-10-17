using UnityEngine;

namespace Yujian.Mining
{
    /// <summary>
    /// 矿石碎掉的表演：炸出几块小碎片。
    /// 只做视觉，不负责销毁矿石本身——矿石照旧由 Ore.Die() 处理。
    /// </summary>
    public class OreShatter : MonoBehaviour
    {
        [SerializeField] private int pieceCount = 10;
        [SerializeField] private float force = 2.5f;
        [SerializeField] private float pieceLifetime = 1.2f;
        [SerializeField] private Vector2 pieceSizeRange = new Vector2(0.08f, 0.2f);

        public void Burst()
        {
            Renderer renderer = GetComponentInChildren<Renderer>();
            Color color = renderer != null ? renderer.material.color : Color.gray;

            for (int i = 0; i < pieceCount; i++)
            {
                SpawnPiece(color);
            }
        }

        private void SpawnPiece(Color color)
        {
            GameObject piece = GameObject.CreatePrimitive(PrimitiveType.Cube);
            piece.name = "OreDebris";

            Collider collider = piece.GetComponent<Collider>();
            if (collider != null) Destroy(collider);

            float size = Random.Range(pieceSizeRange.x, pieceSizeRange.y);
            piece.transform.localScale = Vector3.one * size;
            piece.transform.position = transform.position + Random.insideUnitSphere * 0.25f;
            piece.transform.rotation = Random.rotation;

            Renderer pieceRenderer = piece.GetComponent<Renderer>();
            pieceRenderer.material.color = color;
            if (pieceRenderer.material.HasProperty("_BaseColor"))
            {
                pieceRenderer.material.SetColor("_BaseColor", color);
            }

            Rigidbody body = piece.AddComponent<Rigidbody>();
            body.mass = 0.2f;
            Vector3 direction = (Random.onUnitSphere + Vector3.up * 1.2f).normalized;
            body.AddForce(direction * force, ForceMode.Impulse);
            body.AddTorque(Random.onUnitSphere * force, ForceMode.Impulse);

            Destroy(piece, pieceLifetime);
        }
    }
}