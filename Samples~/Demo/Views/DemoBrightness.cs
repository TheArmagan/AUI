using UdonSharp;
using UnityEngine;
using Armagan.AUI.Generated;   // every compiled view class lives here

/// <summary>
/// An ordinary world script that owns something (here a light) and talks to an AUI view in
/// both directions. This is the whole linking story:
///
///   script -> UI: hold a typed reference to the generated view class and set its properties.
///                 `panel.Brightness = 70f` moves the slider and repaints its label, and does
///                 NOT call the view's handler (setters from code never do).
///   UI -> script: the view's own .cs half holds a typed reference to this script and calls
///                 it from a handler (see SettingsDemo.BrightnessChanged).
///
/// References are assigned in the inspector on the scene instances: a prefab cannot point at a
/// scene object, so the view's `brightness` field is an instance override, like any other.
/// </summary>
[UdonBehaviourSyncMode(BehaviourSyncMode.None)]
public class DemoBrightness : UdonSharpBehaviour
{
    [Tooltip("The light this script dims.")]
    public Light target;

    [Tooltip("The view that shows the slider. Typed as the generated class, so its properties are available.")]
    public SettingsDemo panel;

    [Range(0, 100)] public float percent = 70f;

    private void Start()
    {
        Apply();
        // Push the current value into the UI. Safe before or after the view's own Start:
        // the property stores the value and repaints whatever exists.
        if (panel != null) panel.Brightness = percent;
    }

    /// <summary>Called by the view's BrightnessChanged handler while the slider is dragged.</summary>
    public void SetPercent(float value)
    {
        percent = Mathf.Clamp(value, 0f, 100f);
        Apply();
    }

    private void Apply()
    {
        if (target != null) target.intensity = 0.2f + percent / 100f * 1.8f;
    }
}
