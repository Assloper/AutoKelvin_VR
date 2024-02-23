using System.Collections;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.UI;

public class ChangeKelvin : MonoBehaviour
{
    public Light pointLight;

    public void ToggleLight()
    {
        pointLight.enabled = !pointLight.enabled;
    }

    public void ChangeLightColor(Color color)
    {
        pointLight.color = color;
    }

    public void ChangeColorTemperature(float temperature)
    {
        pointLight.colorTemperature = temperature;
    }
}
