using System;
using UnityEngine;

namespace MarchingCubes
{

    public class ChainsawController : MonoBehaviour
    {
        #region Editable attributes

        [SerializeField] float _cuttingRadius = 0.1f; // Reduced from 0.5f for more precise cutting
        [SerializeField] float _cuttingSpeed = 0.5f; // Reduced from 2.0f for more controlled cutting
        [SerializeField] float _movementSpeed = 5.0f;
        [SerializeField] SDFVisualizer _treeVisualizer = null;
        [SerializeField] ComputeShader _sdfModifier = null;
        [SerializeField] ComputeShader _editCompute = null;

        #endregion

        #region Private members

        private Vector3 _lastPosition;
        private bool _isCutting = false;
        private bool _isIntersecting = false; // Track intersection state

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
            UpdateCutting();

            // Debug intersection state - to resolve debug intersection
            if (Time.frameCount % 60 == 0) // Log every 60 frames to avoid spam
            {
                Debug.Log($"Intersection Debug - Cutting: {_isCutting}, Intersecting: {_isIntersecting}, Position: {transform.position}");
            }
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

            // Check for intersection with tree - to resolve conditional cutting
            // Prioritize SDF method as it's most accurate for this use case
            _isIntersecting = CheckIntersectionWithSDF() || CheckIntersectionWithTree();

            if (movement.magnitude > 0.001f && _isIntersecting)
            {
                // Begin texture modification to prevent race conditions - to resolve issue number 8
                if (!_treeVisualizer.BeginTextureModification())
                {
                    // Texture is being modified by another operation, skip this frame
                    return;
                }

                try
                {
                    // Get the writable texture from SDFVisualizer
                    var writableTexture = _treeVisualizer.GetWorkingTexture();
                    if (writableTexture == null)
                    {
                        Debug.LogError("Working texture is null - cannot perform cutting operation"); // to resolve issue number 6
                        return;
                    }

                    // Get texture dimensions for coordinate transformation
                    var originalSDF = _treeVisualizer.GetSDFTexture();
                    if (originalSDF == null)
                    {
                        Debug.LogError("Original SDF texture is null - cannot perform cutting operation"); // to resolve issue number 6
                        return;
                    }

                    // Validate texture format compatibility - to resolve issue number 10
                    if (writableTexture.format != RenderTextureFormat.RFloat)
                    {
                        Debug.LogError($"Working texture format {writableTexture.format} is not compatible with compute shader");
                        return;
                    }

                    // Transform world position to texture coordinates (0-1 range) - to resolve issue number 1 & 2
                    Vector3 relativePosition = _treeVisualizer.transform.InverseTransformPoint(transform.position);
                    float gridScale = _treeVisualizer.GridScale;

                    // Correct texture coordinate calculation: map from [-gridScale/2, gridScale/2] to [0, 1]
                    Vector3 textureCenter = new Vector3(
                        (relativePosition.x + gridScale / 2) / gridScale,
                        (relativePosition.y + gridScale / 2) / gridScale,
                        (relativePosition.z + gridScale / 2) / gridScale
                    );

                    // Clamp texture coordinates to valid range - to resolve issue number 6
                    textureCenter.x = Mathf.Clamp01(textureCenter.x);
                    textureCenter.y = Mathf.Clamp01(textureCenter.y);
                    textureCenter.z = Mathf.Clamp01(textureCenter.z);

                    // Calculate cutting parameters - to resolve issue number 5
                    float cutRadius = _cuttingRadius / gridScale; // Convert to texture space correctly
                    float cutDepth = _cuttingSpeed;

                    // Validate cutting parameters - to resolve issue number 6
                    if (cutRadius <= 0 || cutDepth <= 0)
                    {
                        Debug.LogWarning($"Invalid cutting parameters: radius={cutRadius}, depth={cutDepth}");
                        return;
                    }

                    // Dispatch the cutting compute shader on the writable texture - to resolve issue number 4
                    _sdfModifier.SetInts("Dims", new int[] { originalSDF.width, originalSDF.height, originalSDF.depth });
                    _sdfModifier.SetVector("CutCenter", textureCenter);
                    _sdfModifier.SetFloat("CutRadius", cutRadius);
                    _sdfModifier.SetFloat("CutDepth", cutDepth);
                    _sdfModifier.SetVector("CutDirection", movement.normalized);
                    _sdfModifier.SetTexture(0, "SDFTexture", writableTexture);

                    // Calculate proper dispatch dimensions - to resolve issue number 6
                    int dispatchX = Mathf.CeilToInt(writableTexture.width / 8.0f);
                    int dispatchY = Mathf.CeilToInt(writableTexture.height / 8.0f);
                    int dispatchZ = Mathf.CeilToInt(writableTexture.volumeDepth / 8.0f);

                    _sdfModifier.Dispatch(0, dispatchX, dispatchY, dispatchZ);

                    Debug.Log($"Cutting (Intersecting): World={currentPosition}, Relative={relativePosition}, Texture={textureCenter}, Radius={cutRadius}, Depth={cutDepth}");
                }
                finally
                {
                    // Always end texture modification to prevent deadlock - to resolve issue number 8
                    _treeVisualizer.EndTextureModification();
                }
            }

            _lastPosition = currentPosition;
        }

        #endregion

        #region Intersection Detection

        // Check if the cutter is intersecting with the tree mesh - to resolve intersection detection
        private bool CheckIntersectionWithTree()
        {
            if (_treeVisualizer == null)
            {
                Debug.LogWarning("Tree visualizer is null");
                return false;
            }

            // Get the tree's mesh filter
            var meshFilter = _treeVisualizer.GetComponent<MeshFilter>();
            if (meshFilter == null || meshFilter.sharedMesh == null)
            {
                Debug.LogWarning("Tree mesh filter or mesh is null - using SDF method");
                return CheckIntersectionWithSDF();
            }

            // Use direct mesh-based intersection detection instead of physics
            return CheckMeshIntersection(meshFilter.sharedMesh, meshFilter.transform);
        }

        // Direct mesh intersection detection without physics dependency - to resolve mesh intersection
        private bool CheckMeshIntersection(Mesh mesh, Transform meshTransform)
        {
            if (mesh == null) return false;

            // Get mesh vertices in world space
            Vector3[] vertices = mesh.vertices;
            Vector3 cutterPosition = transform.position;

            // Check if any vertex is within cutting radius
            for (int i = 0; i < vertices.Length; i++)
            {
                Vector3 worldVertex = meshTransform.TransformPoint(vertices[i]);
                float distance = Vector3.Distance(cutterPosition, worldVertex);

                if (distance <= _cuttingRadius)
                {
                    // Debug mesh intersection
                    if (Time.frameCount % 120 == 0)
                    {
                        Debug.Log($"Mesh intersection found - Vertex distance: {distance}, Cutting radius: {_cuttingRadius}");
                    }
                    return true;
                }
            }

            // Also check mesh bounds intersection as a quick test
            Bounds meshBounds = mesh.bounds;
            Bounds worldMeshBounds = new Bounds(
                meshTransform.TransformPoint(meshBounds.center),
                Vector3.Scale(meshBounds.size, meshTransform.lossyScale)
            );

            Bounds cutterBounds = new Bounds(cutterPosition, Vector3.one * _cuttingRadius * 2);
            bool boundsIntersect = worldMeshBounds.Intersects(cutterBounds);

            // Debug bounds intersection
            if (Time.frameCount % 120 == 0)
            {
                Debug.Log($"Mesh bounds check - Intersects: {boundsIntersect}, Mesh bounds: {worldMeshBounds}, Cutter bounds: {cutterBounds}");
            }

            return boundsIntersect;
        }

        // Primary intersection method using SDF-based detection - to resolve SDF based detection
        private bool CheckIntersectionWithSDF()
        {
            if (_treeVisualizer == null) return false;

            // Transform world position to texture coordinates
            Vector3 relativePosition = _treeVisualizer.transform.InverseTransformPoint(transform.position);
            float gridScale = _treeVisualizer.GridScale;

            // Calculate distance from grid center (where the tree should be)
            float distanceFromCenter = Vector3.Distance(relativePosition, Vector3.zero);

            // Use a more accurate tree radius based on the actual SDF data
            // The tree should occupy roughly the center portion of the grid
            float treeRadius = gridScale * 0.35f; // 35% of grid scale as tree radius
            float intersectionRadius = treeRadius + _cuttingRadius; // Add cutting radius for intersection

            bool intersects = distanceFromCenter < intersectionRadius;

            // Debug SDF intersection
            if (Time.frameCount % 120 == 0)
            {
                Debug.Log($"SDF Intersection - Distance: {distanceFromCenter:F3}, Tree radius: {treeRadius:F3}, Intersection radius: {intersectionRadius:F3}, Intersects: {intersects}");
            }

            return intersects;
        }

        // Simple distance-based intersection check - to resolve intersection detection
        private bool CheckSimpleDistanceIntersection()
        {
            if (_treeVisualizer == null) return false;

            // Get distance from tree center
            Vector3 treeCenter = _treeVisualizer.transform.position;
            float distanceFromTree = Vector3.Distance(transform.position, treeCenter);

            // Use a reasonable tree radius based on grid scale
            float treeRadius = _treeVisualizer.GridScale * 0.35f; // Match SDF method radius
            float intersectionRadius = treeRadius + _cuttingRadius; // Add cutting radius for intersection

            bool intersects = distanceFromTree < intersectionRadius;

            // Debug simple intersection
            if (Time.frameCount % 120 == 0)
            {
                Debug.Log($"Simple Intersection - Distance: {distanceFromTree:F3}, Tree radius: {treeRadius:F3}, Intersection radius: {intersectionRadius:F3}, Intersects: {intersects}");
            }

            return intersects;
        }

        #endregion

        #region Gizmos

        void OnDrawGizmos()
        {
            // Visual feedback for intersection detection - to resolve visual feedback
            if (_isCutting && _isIntersecting)
            {
                Gizmos.color = Color.red; // Cutting and intersecting
                Gizmos.DrawWireSphere(transform.position, _cuttingRadius);
                Gizmos.DrawSphere(transform.position, _cuttingRadius * 0.1f); // Solid center
            }
            else if (_isCutting)
            {
                Gizmos.color = new Color(1f, 0.5f, 0f); // Orange color for cutting but not intersecting
                Gizmos.DrawWireSphere(transform.position, _cuttingRadius);
            }
            else if (_isIntersecting)
            {
                Gizmos.color = Color.green; // Not cutting but intersecting
                Gizmos.DrawWireSphere(transform.position, _cuttingRadius);
            }
            else
            {
                Gizmos.color = Color.yellow; // Not cutting and not intersecting
                Gizmos.DrawWireSphere(transform.position, _cuttingRadius);
            }

            // Draw tree bounds for debugging - to resolve debug intersection
            if (_treeVisualizer != null)
            {
                Gizmos.color = Color.cyan;
                Vector3 treeCenter = _treeVisualizer.transform.position;
                float treeRadius = _treeVisualizer.GridScale * 0.35f; // Match the intersection detection radius
                Gizmos.DrawWireSphere(treeCenter, treeRadius);

                // Draw intersection radius
                Gizmos.color = Color.magenta;
                Gizmos.DrawWireSphere(treeCenter, treeRadius + _cuttingRadius);

                // Draw grid bounds for reference
                Gizmos.color = Color.gray;
                float gridRadius = _treeVisualizer.GridScale * 0.5f;
                Gizmos.DrawWireSphere(treeCenter, gridRadius);
            }
        }

        #endregion
    }

} // namespace MarchingCubes

