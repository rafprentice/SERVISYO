import json
import os
import glob

# Paths
prompts_folder = r"C:\Users\ralph\Servisyo\Prompts"
graph_json_path = r"C:\Users\ralph\Servisyo\graphify-out\graph.json"

if not os.path.exists(graph_json_path):
    print("Error: graphify-out/graph.json not found. Run graphify first.")
    exit(1)

# Load existing Graphify data
with open(graph_json_path, 'r', encoding='utf-8') as f:
    graph_data = json.load(f)

existing_node_ids = {node['id'] for node in graph_data.get('nodes', [])}

# Read markdown prompt files from Obsidian folder
md_files = glob.glob(os.path.join(prompts_folder, "*.md"))

added_count = 0
for filepath in md_files:
    filename = os.path.basename(filepath)
    node_id = f"obsidian_prompt_{filename}"

    with open(filepath, 'r', encoding='utf-8') as pf:
        content = pf.read()

    # Create a new node in graphify
    prompt_node = {
        "id": node_id,
        "label": f"Prompt: {filename}",
        "type": "Obsidian Prompt Note",
        "file": filepath,
        "summary": content[:300] + "..." if len(content) > 300 else content
    }

    if node_id not in existing_node_ids:
        graph_data['nodes'].append(prompt_node)
        existing_node_ids.add(node_id)
        added_count += 1

# Save updated graph JSON
with open(graph_json_path, 'w', encoding='utf-8') as f:
    json.dump(graph_data, f, indent=2)

print(f"Synced {added_count} new Obsidian prompt(s) into Graphify graph data.")