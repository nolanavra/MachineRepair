using UnityEngine;

namespace MachineRepair.Rendering
{
    /// <summary>
    /// Keeps a wire-glow overlay camera aligned with the base camera so both share
    /// projection and transform properties.
    /// </summary>
    [DisallowMultipleComponent]
    public class WireGlowCameraSync : MonoBehaviour
    {
        [Header("Camera References")]
        [SerializeField] private Camera baseCamera;
        [SerializeField] private Camera wireGlowCamera;

        private bool warnedMissingReference;

        private void Reset()
        {
            wireGlowCamera = GetComponent<Camera>();
        }

        private void LateUpdate()
        {
            if (!ValidateCameras())
                return;

            SyncProjection();
            SyncTransform();
        }

        private bool ValidateCameras()
        {
            if (baseCamera != null && wireGlowCamera != null)
            {
                warnedMissingReference = false;
                return true;
            }

            if (!warnedMissingReference)
            {
                Debug.LogWarning($"[{nameof(WireGlowCameraSync)}] Missing camera reference(s) on {name}. Base: {baseCamera}, WireGlow: {wireGlowCamera}.");
                warnedMissingReference = true;
            }

            return false;
        }

        private void SyncProjection()
        {
            wireGlowCamera.orthographic = baseCamera.orthographic;
            if (baseCamera.orthographic)
            {
                wireGlowCamera.orthographicSize = baseCamera.orthographicSize;
            }
            else
            {
                wireGlowCamera.fieldOfView = baseCamera.fieldOfView;
            }

            wireGlowCamera.aspect = baseCamera.aspect;
            wireGlowCamera.nearClipPlane = baseCamera.nearClipPlane;
            wireGlowCamera.farClipPlane = baseCamera.farClipPlane;
        }

        private void SyncTransform()
        {
            wireGlowCamera.transform.SetPositionAndRotation(
                baseCamera.transform.position,
                baseCamera.transform.rotation);
        }
    }
}
