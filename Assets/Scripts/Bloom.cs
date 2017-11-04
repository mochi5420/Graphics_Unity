using UnityEngine;

[ExecuteInEditMode]
[RequireComponent(typeof(Camera))]
[AddComponentMenu("KM Effects/Bloom")]
public class Bloom : MonoBehaviour {

#region Public Properties

    [SerializeField]
    [Range(0.0f, 1.0f)]
    float _threshold = 0.8f;

    [SerializeField]
    [Range(0, 1)]
    [Tooltip("Makes transition between under/over-threshold gradual.")]
    float _softKnee = 0.5f;


    /// Bloom radius
    /// Changes extent of veiling effects in a screen
    /// resolution-independent fashion.
    [SerializeField]
    [Range(1, 8)]
    [Tooltip("Changes extent of veiling effects\n" +
             "in a screen resolution-independent fashion.")]
    float _radius = 2.5f;

    /// Bloom intensity
    /// Blend factor of the result image.
    [SerializeField]
    [Range(0.0f, 2.0f)]
    [Tooltip("Blend factor of the result image.")]
    float _intensity = 0.8f;

    // Iteration
    [SerializeField]
    [Range(1, 20)]
    int kMaxIterations = 16;

    /// High quality mode
    /// Controls filter quality and buffer resolution.
    [SerializeField]
    [Tooltip("Controls filter quality and buffer resolution.")]
    bool _highQuality = true;

    /// Anti-flicker filter
    /// Reduces flashing noise with an additional filter.
    [SerializeField]
    [Tooltip("Reduces flashing noise with an additional filter.")]
    bool _antiFlicker = true;
    
#endregion


#region Private Members

    Shader _shader;
    Material _material;

    RenderTexture[] _blurBuffer1;
    RenderTexture[] _blurBuffer2;

    float GammaToLinear(float x)
    {
#if UNITY_5_3_OR_NEWER
        return Mathf.GammaToLinearSpace(x);
#else
            if (x <= 0.04045f)
                return x / 12.92f;
            else
                return Mathf.Pow((x + 0.055f) / 1.055f, 2.4f);
#endif
    }

#endregion

#region MonoBehaviour Functions

    void OnEnable()
    {
        var shader = Shader.Find("KMShaders/Bloom");
        _material = new Material(shader);
        _material.hideFlags = HideFlags.DontSave;
        _blurBuffer1 = new RenderTexture[kMaxIterations];
        _blurBuffer2 = new RenderTexture[kMaxIterations];
    }

    void OnDisable()
    {
        DestroyImmediate(_material);
    }

    void OnRenderImage(RenderTexture src, RenderTexture dst)
    {
        bool useRGBM = Application.isMobilePlatform;

        // source texture size
        var tw = src.width;
        var th = src.height;

        // halve the texture size for the low quality mode
        if (!_highQuality)
        {
            tw /= 2;
            th /= 2;
        }

        // blur buffer format
        var rtFormat = useRGBM ?
            RenderTextureFormat.Default : RenderTextureFormat.DefaultHDR;

        // determine the iteration count
        float logh = Mathf.Log(th, 2) + _radius - 8;
        var logh_i = (int)logh;
        int iterations = Mathf.Clamp(logh_i, 1, kMaxIterations);

        // update the shader properties
        float lthresh = GammaToLinear(_threshold);
        _material.SetFloat("_Threshold", lthresh);

        float knee = lthresh * _softKnee + 1e-5f;
        var curve = new Vector3(lthresh - knee, knee * 2, 0.25f / knee);
        _material.SetVector("_Curve", curve);

        bool pfo = !_highQuality && _antiFlicker;
        _material.SetFloat("_PrefilterOffs", pfo ? -0.5f : 0.0f);

        _material.SetFloat("_SampleScale", 0.5f + logh - logh_i);
        _material.SetFloat("_Intensity", _intensity);

        // prefilter pass
        var prefiltered = RenderTexture.GetTemporary(tw, th, 0, rtFormat);
        var pass = _antiFlicker ? 1 : 0;
        Graphics.Blit(src, prefiltered, _material, pass);

        // construct a mip pyramid
        RenderTexture last = prefiltered;
        for (var level = 0; level < iterations; level++)
        {
            _blurBuffer1[level] = RenderTexture.GetTemporary(
                last.width / 2, last.height / 2, 0, rtFormat
            );

            pass = (level == 0) ? (_antiFlicker ? 3 : 2) : 4;
            Graphics.Blit(last, _blurBuffer1[level], _material, pass);

            last = _blurBuffer1[level];
        }

        // upsample and combine loop
        for (var level = iterations - 2; level >= 0; level--)
        {
            RenderTexture basetex = _blurBuffer1[level];
            _material.SetTexture("_BaseTex", basetex);

            _blurBuffer2[level] = RenderTexture.GetTemporary(
                basetex.width, basetex.height, 0, rtFormat
            );

            pass = _highQuality ? 6 : 5;
            Graphics.Blit(last, _blurBuffer2[level], _material, pass);
            last = _blurBuffer2[level];
        }

        // finish process
        _material.SetTexture("_BaseTex", src);
        pass = _highQuality ? 8 : 7;
        Graphics.Blit(last, dst, _material, pass);

        // release the temporary buffers
        for (var i = 0; i < kMaxIterations; i++)
        {
            if (_blurBuffer1[i] != null)
                RenderTexture.ReleaseTemporary(_blurBuffer1[i]);

            if (_blurBuffer2[i] != null)
                RenderTexture.ReleaseTemporary(_blurBuffer2[i]);

            _blurBuffer1[i] = null;
            _blurBuffer2[i] = null;
        }

        RenderTexture.ReleaseTemporary(prefiltered);
    }

#endregion
}
