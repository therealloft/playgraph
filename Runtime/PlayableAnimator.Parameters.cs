namespace Playgraph
{
    public sealed partial class PlayableAnimator
    {
        public void SetFloat(string parameterName, float value)
        {
            parameterStore.SetFloat(parameterName, value);
        }

        public float GetFloat(string parameterName)
        {
            return parameterStore.GetFloat(parameterName);
        }

        public void SetBool(string parameterName, bool value)
        {
            parameterStore.SetBool(parameterName, value);
        }

        public bool GetBool(string parameterName)
        {
            return parameterStore.GetBool(parameterName);
        }

        public void SetInteger(string parameterName, int value)
        {
            parameterStore.SetInteger(parameterName, value);
        }

        public int GetInteger(string parameterName)
        {
            return parameterStore.GetInteger(parameterName);
        }

        public void SetEnum(string parameterName, string value)
        {
            parameterStore.SetEnum(parameterName, value);
        }

        public string GetEnum(string parameterName)
        {
            return parameterStore.GetEnum(parameterName);
        }

        public void SetTrigger(string parameterName)
        {
            parameterStore.SetTrigger(parameterName);
        }

        public void ResetTrigger(string parameterName)
        {
            parameterStore.ResetTrigger(parameterName);
        }

        public bool GetTrigger(string parameterName)
        {
            return parameterStore.GetTrigger(parameterName);
        }
    }
}
