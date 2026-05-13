#!/bin/bash

# Define the replacements as pairs
declare -A replacements=(
    ["DataRootDirectory"]="DataManagement.DataRootDirectory"
    ["LocalAiAssetsRoot"]="DataManagement.LocalAiAssetsRoot"
    ["LlamaCppBaseUrl"]="Llm.LlamaCppBaseUrl"
    ["LlamaCppEnabled"]="Llm.LlamaCppEnabled"
    ["OpenAiBaseUrl"]="Llm.OpenAiBaseUrl"
    ["OpenAiApiKey"]="Llm.OpenAiApiKey"
    ["OpenAiEnabled"]="Llm.OpenAiEnabled"
    ["DefaultModel"]="Llm.DefaultModel"
    ["MaxTokens"]="Llm.MaxTokens"
    ["EmbeddingModel"]="Rag.EmbeddingModel"
    ["RagEnabled"]="Rag.Enabled"
    ["RagServiceUrl"]="Rag.ServiceUrl"
    ["RagRerankerEnabled"]="Rag.RerankerEnabled"
    ["RagRerankerAutoDownload"]="Rag.RerankerAutoDownload"
    ["RagRerankerModelPath"]="Rag.RerankerModelPath"
    ["RagRerankerMaxLength"]="Rag.RerankerMaxLength"
    ["RagRerankerMaxCandidates"]="Rag.RerankerMaxCandidates"
    ["TtsEnabled"]="Tts.Enabled"
    ["TtsServiceUrl"]="Tts.ServiceUrl"
    ["TtsSpeaker"]="Tts.Speaker"
    ["TtsPythonPath"]="Tts.PythonPath"
    ["TtsScriptPath"]="Tts.ScriptPath"
    ["TtsModelDirectory"]="Tts.ModelDirectory"
    ["TtsOutputDirectory"]="Tts.OutputDirectory"
    ["TtsDevice"]="Tts.Device"
    ["TtsModelVersion"]="Tts.ModelVersion"
    ["TtsPreload"]="Tts.Preload"
    ["TtsVoiceDirectory"]="Tts.VoiceDirectory"
    ["VoiceProvider"]="Tts.VoiceProvider"
)

# Create the sed expression
sed_expr=""
for old in "${!replacements[@]}"; do
    new="${replacements[$old]}"
    # Match settings.PropertyName but keep settings. part
    # We use \b to match word boundary to avoid partial matches
    sed_expr+="s/settings\.$old\b/settings.$new/g; "
    # Also handle _settings.Settings.PropertyName
    sed_expr+="s/Settings\.$old\b/Settings.$new/g; "
done

# Run find and apply sed to all .cs files in src
find src -name "*.cs" -exec sed -i "$sed_expr" {} +

echo "Replacements complete."
