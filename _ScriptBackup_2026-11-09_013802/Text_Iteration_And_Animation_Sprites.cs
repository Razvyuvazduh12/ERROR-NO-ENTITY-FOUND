using UnityEngine;
    using System.Collections;
    using UnityEngine.InputSystem;

    [ExecuteAlways]
    public class Text_Iteration_And_Animation_Sprites : MonoBehaviour
    {   
        public string spriteSheetPath_CAPS = "Letters/fontcaps";
        public int frameNumber = 24; // used for the static editor preview only
        public string spriteName = "A";
        public Color letterTint = Color.white;
        public int totalFrames = 24;
        public float fps = 10f;
        public bool loop = false;
        public bool clickToDisappear = true; // false when used as a button letter, or a question's letters — something else controls disappearance instead

        private SpriteRenderer sr;
        private Sprite[] frames;
        private Coroutine currentAnimation;
        private bool hasDisappeared = false;
        public bool disappearFinished = false; // true only once PlayDisappearAnimation() actually finishes — for callers that need to wait on the real animation, not an estimate

        void Start()
        {
            SpawnSprite(frameNumber, spriteName);

            if (Application.isPlaying)
            {
                LoadAllFrames();
                currentAnimation = StartCoroutine(PlayAnimation());
            }
        }

        void Update()
        {
            if (Application.isPlaying && clickToDisappear && !hasDisappeared && Mouse.current != null && Mouse.current.leftButton.wasPressedThisFrame)
            {
                TriggerDisappear();
            }
        }

        void OnValidate()
        {
            #if UNITY_EDITOR
            UnityEditor.EditorApplication.delayCall += () =>
            {
                if (this == null) return;
                if (!gameObject.activeInHierarchy) return;
                SpawnSprite(frameNumber, spriteName);
            };
            #endif
        }

        // Public wrapper so callers (like ResponseButtonGroup) can force the sprite to spawn
        // immediately and get the real SpriteRenderer back, instead of waiting on Start() timing.
        public SpriteRenderer EnsureSpawned()
        {
            SpawnSprite(frameNumber, spriteName);
            return GetComponentInChildren<SpriteRenderer>();
        }

        // Starts the disappear animation from an external trigger (e.g. SentenceBuilder reacting
        // to a chosen response) rather than only from this object's own click detection.
        public void TriggerDisappear()
        {
            if (hasDisappeared) return;
            hasDisappeared = true;
            disappearFinished = false;
            if (currentAnimation != null) StopCoroutine(currentAnimation);
            currentAnimation = StartCoroutine(PlayDisappearAnimation());
        }

        void SpawnSprite(int frame, string letter)
        {
            ClearOldLetters(letter);

            Transform existing = transform.Find("Letter_" + letter);
            GameObject child;

            if (existing == null)
            {
                Sprite target = LoadFrameSprite(frame, letter);
                if (target == null) return;

                child = new GameObject("Letter_" + letter);
                child.transform.SetParent(transform, false);
                sr = child.AddComponent<SpriteRenderer>();
                sr.sprite = target;
            }
            else
            {
                child = existing.gameObject;
                sr = child.GetComponent<SpriteRenderer>();
            }

            sr.color = letterTint;
        }

        Sprite LoadFrameSprite(int frame, string letter)
        {
            string frameStr = frame.ToString("00");
            string path = spriteSheetPath_CAPS + "/" + frameStr;
            Sprite[] allSprites = Resources.LoadAll<Sprite>(path);
            foreach (Sprite s in allSprites)
            {
                if (s.name == letter) return s;
            }
            Debug.LogError($"No sprite named '{letter}' found in {path}");
            return null;
        }

        void LoadAllFrames()
        {
            frames = new Sprite[totalFrames];
            for (int i = 0; i < totalFrames; i++)
            {
                frames[i] = LoadFrameSprite(i + 1, spriteName);
            }
        }

        IEnumerator PlayAnimation()
        {
            float delay = 1f / fps;

            do
            {
                for (int i = 0; i < totalFrames; i++)
                {
                    if (frames[i] != null) sr.sprite = frames[i];
                    yield return new WaitForSeconds(delay);
                }
            } while (loop && !hasDisappeared);
        }

        IEnumerator PlayDisappearAnimation()
        {
            float delay = 1f / fps;

            // Walk the SAME frame array backwards: index (totalFrames-1) is frame 24, index 0 is frame 1
            for (int i = totalFrames - 1; i >= 0; i--)
            {
                if (frames[i] != null) sr.sprite = frames[i];
                yield return new WaitForSeconds(delay);
            }

            // One step "past" frame 1 — nothing left to draw   
            sr.sprite = null;
            disappearFinished = true;
        }

        void ClearOldLetters(string keep)
        {
            for (int i = transform.childCount - 1; i >= 0; i--)
            {
                Transform childT = transform.GetChild(i);
                if (childT.name.StartsWith("Letter_") && childT.name != "Letter_" + keep)
                {
                    if (Application.isPlaying)
                        Destroy(childT.gameObject);
                    else
                        DestroyImmediate(childT.gameObject);
                }
            }
        }
    }