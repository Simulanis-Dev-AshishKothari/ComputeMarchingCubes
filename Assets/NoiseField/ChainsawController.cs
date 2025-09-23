using System;
using UnityEngine;

namespace MarchingCubes
{

    public class ChainsawController : MonoBehaviour
    {
        #region Editable attributes

        [SerializeField] float _cuttingRadius = 0.5f;
        [SerializeField] float _cuttingSpeed = 2.0f;
        [SerializeField] float _movementSpeed = 5.0f;
        [SerializeField] SDFVisualizer _treeVisualizer = null;
        [SerializeField] ComputeShader _sdfModifier = null;
        [SerializeField] ComputeShader _editCompute = null;

        #endregion

        #region Private members

        private Vector3 _lastPosition;
        private bool _isCutting = false;

        #endregion

        #region MonoBehaviour implementation

        void Start()
        {
            _lastPosition = transform.position;
            Debug.Log("Chainsaw initialized - will get writable texture from SDFVisualizer");
        }

        void OnDestroy()
        {
            // No cleanup needed - SDFVisualizer manages the writable texture
        }

        void Update()
        {
            HandleInput();
            UpdateCutting2();
        }

        #endregion

        #region Input handling

        void HandleInput()
        {
            // Movement
            Vector3 movement = Vector3.zero;
            if (Input.GetKey(KeyCode.W)) movement += Vector3.forward;
            if (Input.GetKey(KeyCode.S)) movement += Vector3.back;
            if (Input.GetKey(KeyCode.A)) movement += Vector3.left;
            if (Input.GetKey(KeyCode.D)) movement += Vector3.right;
            if (Input.GetKey(KeyCode.Q)) movement += Vector3.up;
            if (Input.GetKey(KeyCode.E)) movement += Vector3.down;

            transform.Translate(movement * _movementSpeed * Time.deltaTime);

            // Cutting toggle
            _isCutting = Input.GetMouseButton(0);
        }

        #endregion

        #region Cutting logic

        void UpdateCutting2()
        {
            if (!_isCutting || _editCompute == null || _treeVisualizer == null)
                return;

            float boundSize = _treeVisualizer.GridScale;
            int editTextureSize = _treeVisualizer.GetSDFTexture().width;
            float editPixelWorldSize = boundSize * editTextureSize;
            int editRadius = Mathf.CeilToInt(_cuttingRadius / editPixelWorldSize);
            Debug.Log(editPixelWorldSize + " || " + editRadius);

            Vector3 offsetedPoint = _treeVisualizer.transform.InverseTransformPoint(transform.position);
            Debug.Log($"offsetedPoint: {offsetedPoint}");
            float tx = Mathf.Clamp01((offsetedPoint.x + boundSize / 2) / boundSize);
            float ty = Mathf.Clamp01((offsetedPoint.y + boundSize / 2) / boundSize);
            float tz = Mathf.Clamp01((offsetedPoint.z + boundSize / 2) / boundSize);

            int editX = Mathf.RoundToInt(tx * (editTextureSize - 1));
            int editY = Mathf.RoundToInt(ty * (editTextureSize - 1));
            int editZ = Mathf.RoundToInt(tz * (editTextureSize - 1));
            Debug.Log($"editX: {editX}, editY: {editY}, editZ: {editZ}");

            _editCompute.SetFloat("weight", 1.0f);
            _editCompute.SetFloat("deltaTime", Time.deltaTime);
            _editCompute.SetInts("brushCentre", editX, editY, editZ);
            _editCompute.SetInt("brushRadius", editRadius);

            _editCompute.SetInt("size", editTextureSize);
            _editCompute.SetTexture(0, "EditTexture", _treeVisualizer.GetWorkingTexture());
            _editCompute.DispatchThreads(0, editTextureSize / 8, editTextureSize / 8, editTextureSize / 8);

        }

        void UpdateCutting()
        {
            if (!_isCutting || _sdfModifier == null || _treeVisualizer == null)
                return;

            Vector3 currentPosition = transform.position;
            Vector3 movement = currentPosition - _lastPosition;

            if (movement.magnitude > 0.001f)
            {
                // Get the writable texture from SDFVisualizer
                var writableTexture = _treeVisualizer.GetWorkingTexture();
                if (writableTexture == null) return;

                // Get texture dimensions for coordinate transformation
                var originalSDF = _treeVisualizer.GetSDFTexture();
                if (originalSDF == null) return;

                // Transform world position to texture coordinates (0-1 range)
                // First, get the relative position from tree center
                Vector3 treeCenter = _treeVisualizer.WorldPosition;

                Vector3 relativePosition = _treeVisualizer.transform.InverseTransformPoint(transform.position);

                float gridScale = _treeVisualizer.GridScale;
                // Convert world position to texture coordinates (0-1 range)
                Vector3 textureCenter = new Vector3(
                    (relativePosition.x / (gridScale * originalSDF.width)) + 0.5f,
                    (relativePosition.y / (gridScale * originalSDF.height)) + 0.5f,
                    (relativePosition.z / (gridScale * originalSDF.depth)) + 0.5f
                );

                // Calculate cutting parameters
                float cutRadius = _cuttingRadius / (gridScale * originalSDF.width); // Convert to texture space
                float cutDepth = _cuttingSpeed;

                // Dispatch the cutting compute shader on the writable texture
                _sdfModifier.SetInts("Dims", new int[] { originalSDF.width, originalSDF.height, originalSDF.depth });
                _sdfModifier.SetVector("CutCenter", textureCenter);
                _sdfModifier.SetFloat("CutRadius", cutRadius);
                _sdfModifier.SetFloat("CutDepth", cutDepth);
                _sdfModifier.SetVector("CutDirection", movement.normalized);
                _sdfModifier.SetTexture(0, "SDFTexture", writableTexture);
                _sdfModifier.DispatchThreads(0, writableTexture.width / 8, writableTexture.height / 8, writableTexture.volumeDepth / 8);

                Debug.Log($"Tree center: {treeCenter}, Chainsaw world: {currentPosition}, Relative: {relativePosition}, Texture pos: {textureCenter}, radius: {cutRadius}");
            }

            _lastPosition = currentPosition;
        }

        #endregion

        #region Gizmos

        void OnDrawGizmos()
        {
            if (_isCutting)
            {
                Gizmos.color = Color.red;
                Gizmos.DrawWireSphere(transform.position, _cuttingRadius);
            }
            else
            {
                Gizmos.color = Color.yellow;
                Gizmos.DrawWireSphere(transform.position, _cuttingRadius);
            }
        }

        #endregion
    }

} // namespace MarchingCubes
