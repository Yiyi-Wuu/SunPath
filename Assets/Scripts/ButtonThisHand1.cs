using System.Collections;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.UI;

public class ButtonThisHand1 : MonoBehaviour
{

    [SerializeField] Button playButton;
    [SerializeField] Button calculateButton;
    [SerializeField] SunMotion sunmotion;
    [SerializeField] ShadowPathFinderNavMesh1 shadow;

    private void Start()
    {
        playButton.onClick.AddListener(() => { sunmotion.isPlay = true; });
        calculateButton.onClick.AddListener(() => {
        StopAllCoroutines();
            StartCoroutine(delay_hand());
        
        
        });

    }


    IEnumerator delay_hand()
    {
        sunmotion.isPlay = false;
        yield return new WaitForSeconds(0.2f);
        shadow.RunOptimization();
    }




}
