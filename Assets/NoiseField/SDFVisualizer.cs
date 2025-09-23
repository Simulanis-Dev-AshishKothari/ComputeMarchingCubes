using UnityEngine;

namespace MarchingCubes
{

    sealed class SDFVisualizer : MonoBehaviour
    {
        #region Editable attributes

        [SerializeField] Vector3Int _dimensions = new Vector3Int(64, 32, 64);
        [SerializeField] float _gridScale = 4.0f / 64;
        [SerializeField] int _triangleBudget = 65536;
        [SerializeField] float _targetValue = 0;
        [SerializeField] Texture3D _sdfTexture = null;
        [SerializeField] Texture3D _sdfTexture2 = null;
        [SerializeField] bool _showGridGizmos = true;
        [SerializeField] Color _gridColor = Color.yellow;

        #endregion

        #region Project asset references

        [SerializeField] ComputeShader _sdfCompute = null;
        [SerializeField] ComputeShader _builderCompute = null;

        #endregion

        #region Private members

        int VoxelCount => _dimensions.x * _dimensions.y * _dimensions.z;

        ComputeBuffer _voxelBuffer;
        MeshBuilder _builder;
        [SerializeField] RenderTexture _workingSDFTexture;

        #endregion

        #region MonoBehaviour implementation

        void Start()
        {
            if (_sdfTexture == null)
            {
                Debug.LogError("SDF Texture is not assigned!");
                return;
            }

            if (_sdfTexture.width != _dimensions.x ||
                _sdfTexture.height != _dimensions.y ||
                _sdfTexture.depth != _dimensions.z)
            {
                Debug.LogError($"SDF Texture dimensions ({_sdfTexture.width}x{_sdfTexture.height}x{_sdfTexture.depth}) " +
                              $"do not match specified dimensions ({_dimensions.x}x{_dimensions.y}x{_dimensions.z})");
                return;
            }

            // // Create working render texture for dynamic modifications
            _workingSDFTexture = new RenderTexture(_dimensions.x, _dimensions.y, 0, RenderTextureFormat.RFloat);
            _workingSDFTexture.dimension = UnityEngine.Rendering.TextureDimension.Tex3D;
            _workingSDFTexture.volumeDepth = _dimensions.z;

            _workingSDFTexture.enableRandomWrite = true;
            _workingSDFTexture.Create();

            // Initialize working texture with original SDF data using compute shader
            InitializeWorkingTextureWithOriginalSDF();

            _voxelBuffer = new ComputeBuffer(VoxelCount, sizeof(float));
            _builder = new MeshBuilder(_dimensions, _triangleBudget, _builderCompute);
        }

        void OnDestroy()
        {
            _voxelBuffer?.Dispose();
            _builder?.Dispose();
            _workingSDFTexture?.Release();
        }

        void Update()
        {
            if (_sdfTexture == null || _voxelBuffer == null || _builder == null || _workingSDFTexture == null)
                return;

            // Always use the working texture (initialized with original SDF data)
            _sdfCompute.SetInts("Dims", _dimensions);
            _sdfCompute.SetFloat("Scale", _gridScale);
            _sdfCompute.SetTexture(0, "SDFTexture", _workingSDFTexture);
            _sdfCompute.SetBuffer(0, "Voxels", _voxelBuffer);
            _sdfCompute.DispatchThreads(0, _dimensions);

            // Isosurface reconstruction
            _builder.BuildIsosurface(_voxelBuffer, _targetValue, _gridScale);
            GetComponent<MeshFilter>().sharedMesh = _builder.Mesh;
        }

        void OnDrawGizmos()
        {
            if (_showGridGizmos && _builder != null)
            {
                _builder.DrawGridGizmos(_gridScale, _gridColor, transform.position);
            }
        }

        #endregion

        #region Private methods

        private void InitializeWorkingTextureWithOriginalSDF()
        {
            if (_sdfCompute != null && _sdfTexture != null && _workingSDFTexture != null)
            {
                // Create a temporary buffer to hold the SDF data
                var tempBuffer = new ComputeBuffer(VoxelCount, sizeof(float));

                // Copy original SDF to buffer using SDFToVolume kernel
                _sdfCompute.SetInts("Dims", _dimensions);
                _sdfCompute.SetFloat("Scale", _gridScale);
                _sdfCompute.SetTexture(0, "SDFTexture", _sdfTexture);
                _sdfCompute.SetBuffer(0, "Voxels", tempBuffer);
                _sdfCompute.DispatchThreads(0, _dimensions);

                // Now copy buffer to working texture using a different approach
                // We'll use the SDFToVolume compute shader but with the working texture as output
                // This requires modifying the compute shader to support writing to RenderTexture
                // For now, let's use a simpler approach - copy the buffer data to the working texture
                CopyBufferToRenderTexture(tempBuffer, _workingSDFTexture);

                tempBuffer.Dispose();
                Debug.Log("Working texture initialized with original SDF data");
            }
        }
        
        [ContextMenu("SDF 2")]
        private void InitializeWorkingTextureWithOriginalSDF2()
        {
            if (_sdfCompute != null && _sdfTexture != null && _workingSDFTexture != null)
            {
                // Create a temporary buffer to hold the SDF data
                var tempBuffer = new ComputeBuffer(VoxelCount, sizeof(float));

                // Copy original SDF to buffer using SDFToVolume kernel
                _sdfCompute.SetInts("Dims", _dimensions);
                _sdfCompute.SetFloat("Scale", _gridScale);
                _sdfCompute.SetTexture(0, "SDFTexture", _sdfTexture2);
                _sdfCompute.SetBuffer(0, "Voxels", tempBuffer);
                _sdfCompute.DispatchThreads(0, _dimensions);

                // Now copy buffer to working texture using a different approach
                // We'll use the SDFToVolume compute shader but with the working texture as output
                // This requires modifying the compute shader to support writing to RenderTexture
                // For now, let's use a simpler approach - copy the buffer data to the working texture
                CopyBufferToRenderTexture(tempBuffer, _workingSDFTexture);

                tempBuffer.Dispose();
                Debug.Log("Working texture initialized with original SDF data");
            }
        }

        private void CopyBufferToRenderTexture(ComputeBuffer buffer, RenderTexture renderTexture)
        {
            // This is a simplified approach - we'll use the SDFToVolume compute shader
            // but modify it to write directly to the render texture
            // For now, we'll use Graphics.CopyTexture with a workaround
            try
            {
                // Create a temporary Texture3D from the buffer data
                var tempTexture3D = new Texture3D(_dimensions.x, _dimensions.y, _dimensions.z, TextureFormat.RFloat, false);
                var data = new float[VoxelCount];
                buffer.GetData(data);

                // Convert float[] to Color[] for SetPixels
                var colorData = new Color[VoxelCount];
                for (int i = 0; i < VoxelCount; i++)
                {
                    colorData[i] = new Color(data[i], 0, 0, 1); // Store SDF value in red channel
                }

                tempTexture3D.SetPixels(colorData);
                tempTexture3D.Apply();

                // Copy to render texture
                Graphics.CopyTexture(tempTexture3D, renderTexture);

                // Clean up
                Object.DestroyImmediate(tempTexture3D);
                Debug.Log("Buffer copied to render texture successfully");
            }
            catch (System.Exception e)
            {
                Debug.LogError($"Failed to copy buffer to render texture: {e.Message}");
            }
        }

        #endregion

        #region Public methods

        public Texture3D GetSDFTexture()
        {
            return _sdfTexture;
        }

        public RenderTexture GetWorkingTexture()
        {
            return _workingSDFTexture;
        }

        public ComputeShader GetSDFComputeShader()
        {
            return _sdfCompute;
        }

        public float GridScale => _gridScale;
        public Vector3 WorldPosition => transform.position;

        #endregion
    }

} // namespace MarchingCubes
