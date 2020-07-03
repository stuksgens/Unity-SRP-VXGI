﻿using System.Collections.ObjectModel;
using UnityEngine;
using UnityEngine.Rendering;

public class VXGIRenderPipeline : RenderPipeline {
  public static bool isD3D11Supported => _D3D11DeviceType.Contains(SystemInfo.graphicsDeviceType);

  public PerObjectData PerObjectData { get; }

  static readonly ReadOnlyCollection<GraphicsDeviceType> _D3D11DeviceType = new ReadOnlyCollection<GraphicsDeviceType>(new[] {
    GraphicsDeviceType.Direct3D11,
    GraphicsDeviceType.Direct3D12,
    GraphicsDeviceType.XboxOne,
    GraphicsDeviceType.XboxOneD3D12
  });

  CommandBuffer _command;
  FilteringSettings _filteringSettings;
  ScriptableCullingParameters _cullingParameters;
  VXGIRenderer _renderer;

  public static void TriggerCameraCallback(Camera camera, string message, Camera.CameraCallback callback) {
    camera.SendMessage(message, SendMessageOptions.DontRequireReceiver);
    if (callback != null) callback(camera);
  }

  public VXGIRenderPipeline(VXGIRenderPipelineAsset asset) {
    _renderer = new VXGIRenderer(this);
    _command = new CommandBuffer() { name = "VXGI.RenderPipeline" };
    _filteringSettings = FilteringSettings.defaultValue;

    PerObjectData = asset.perObjectData;
    Shader.globalRenderPipeline = "VXGI";
    GraphicsSettings.lightsUseLinearIntensity = true;
    GraphicsSettings.useScriptableRenderPipelineBatching = asset.SRPBatching;
  }

  protected override void Dispose(bool disposing) {
    base.Dispose(disposing);

    _command.Dispose();

    Shader.globalRenderPipeline = string.Empty;
  }

  protected override void Render(ScriptableRenderContext renderContext, Camera[] cameras) {
    var mainCamera = Camera.main;

    BeginFrameRendering(renderContext, cameras);

    foreach (var camera in cameras) {
      BeginCameraRendering(renderContext, camera);

      if (camera.TryGetComponent<VXGI>(out var vxgi) && vxgi.isActiveAndEnabled) {
        vxgi.Render(renderContext, _renderer);
      } else {
#if UNITY_EDITOR
        if (camera.cameraType == CameraType.SceneView) {
          ScriptableRenderContext.EmitWorldGeometryForSceneView(camera);
        }

        if (mainCamera != null && mainCamera.TryGetComponent<VXGI>(out vxgi) && vxgi.isActiveAndEnabled) {
          vxgi.Render(renderContext, camera, _renderer);
        } else {
          RenderFallback(renderContext, camera);
        }
#else
        RenderFallback(renderContext, camera);
#endif
      }

      EndCameraRendering(renderContext, camera);
    }

    EndFrameRendering(renderContext, cameras);

    renderContext.Submit();
  }

  void RenderFallback(ScriptableRenderContext renderContext, Camera camera) {
    TriggerCameraCallback(camera, "OnPreRender", Camera.onPreRender);

    _command.ClearRenderTarget(true, true, Color.black);
    renderContext.ExecuteCommandBuffer(_command);
    _command.Clear();

    TriggerCameraCallback(camera, "OnPreCull", Camera.onPreCull);

    if (!camera.TryGetCullingParameters(out _cullingParameters)) return;

    var cullingResults = renderContext.Cull(ref _cullingParameters);
    var drawingSettings = new DrawingSettings { perObjectData = PerObjectData };
    drawingSettings.SetShaderPassName(0, ShaderTagIDs.Deferred);
    drawingSettings.SetShaderPassName(1, ShaderTagIDs.PrepassBase);
    drawingSettings.SetShaderPassName(2, ShaderTagIDs.Always);
    drawingSettings.SetShaderPassName(3, ShaderTagIDs.Vertex);
    drawingSettings.SetShaderPassName(4, ShaderTagIDs.VertexLMRGBM);
    drawingSettings.SetShaderPassName(5, ShaderTagIDs.VertexLM);

    renderContext.SetupCameraProperties(camera);
    renderContext.DrawRenderers(cullingResults, ref drawingSettings, ref _filteringSettings);
    renderContext.DrawSkybox(camera);

    TriggerCameraCallback(camera, "OnPostRender", Camera.onPostRender);
  }
}
