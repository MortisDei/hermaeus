# Oghma Infinium — FastAPI Sidecar

Drop `oi_api.py` into the root of your `oghma-infinium` repo to expose `oi`
as a REST service that Aether can call.

## oi_api.py

```python
from fastapi import FastAPI, HTTPException
from pydantic import BaseModel
import time
from src.query import run_query
from src.config import RetrievalConfig

app = FastAPI(title="oi sidecar", version="1.0")

DATASETS = ["skyrim", "australia"]  # add yours here

class QueryRequest(BaseModel):
    dataset: str
    question: str
    top_k: int = 5

@app.get("/health")
def health():
    return {"status": "ok"}

@app.get("/datasets")
def datasets():
    return DATASETS

@app.post("/query")
def query(req: QueryRequest):
    if req.dataset not in DATASETS:
        raise HTTPException(status_code=404, detail=f"Unknown dataset: {req.dataset}")
    t0 = time.time()
    result = run_query(
        dataset=req.dataset,
        question=req.question,
        top_k=req.top_k,
    )
    total = (time.time() - t0) * 1000
    return {
        "answer": result.answer,
        "sources": [
            {
                "title": c.metadata.get("source", ""),
                "type":  c.metadata.get("type", ""),
                "url":   c.metadata.get("url", ""),
                "score": round(c.score, 4),
            }
            for c in result.chunks
        ],
        "retrieval_ms":  result.timings.get("retrieval_ms", 0),
        "generation_ms": result.timings.get("generation_ms", total),
    }
```

## Start it

```bash
cd oghma-infinium
pip install fastapi uvicorn
uvicorn oi_api:app --host 127.0.0.1 --port 8765
```

## Enable in Aether

Settings → RAG → Enable → URL: `http://localhost:8765`

Aether will health-check it on startup and show the dataset picker in the chat toolbar.
