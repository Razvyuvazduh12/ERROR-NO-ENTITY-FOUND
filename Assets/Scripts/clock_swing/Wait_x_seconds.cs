using UnityEngine;

public class Wait_x_seconds : MonoBehaviour
{
    [SerializeField] private float duration = 5f; 
    [SerializeField] private SentenceBuilder sentenceBuilder;
    private float currentTime;
    private bool isTimerRunning = false;
    public bool timer_finished = false; 

    void Start()
    {
        ResetTimer();
    }

    void Update()
    {
        if (!isTimerRunning) return;

        // Scade timpul trecut de la ultimul frame
        currentTime -= Time.deltaTime;
        if (currentTime <= 0)
        {
            
            currentTime = 0;
            isTimerRunning = false;
            TimerFinished();
        }
    }

    public void ResetTimer()
    {
        currentTime = duration;
        isTimerRunning = true;
        
    }

    private void TimerFinished()
    {
        Debug.Log("done");
        sentenceBuilder.Reveal();   
    }
}
