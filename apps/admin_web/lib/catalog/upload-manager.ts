/**
 * T010 — Upload manager.
 *
 * v1 ships a lightweight resumable-upload manager built on `fetch` +
 * IndexedDB persistence (via the `idb` dep already in package.json).
 * Uppy + AWS-S3 plugins are deferred — the lighter shim is enough for
 * spec 005's signed-URL PUT flow and keeps the bundle below the SC-008
 * 200KB initial budget.
 */
import { openDB, type IDBPDatabase } from "idb";

export interface UploadHandle {
  id: string;
  filename: string;
  mediaKind: "image" | "document";
  totalBytes: number;
  uploadedBytes: number;
  status: "pending" | "uploading" | "succeeded" | "failed";
  error?: string;
}

interface PersistedHandle {
  id: string;
  filename: string;
  mediaKind: "image" | "document";
  totalBytes: number;
  uploadedBytes: number;
  status: UploadHandle["status"];
  signedUrl: string;
  error?: string;
}

const DB_NAME = "admin_catalog_uploads";
const STORE = "handles";

export interface UploadManagerDeps {
  /** Issues a signed PUT URL for the file (proxied to spec 003 storage). */
  issueSignedUrl: (input: {
    filename: string;
    contentType: string;
    mediaKind: "image" | "document";
  }) => Promise<{ uploadId: string; signedUrl: string }>;
}

export class UploadManager {
  constructor(private readonly deps: UploadManagerDeps) {}

  private dbPromise: Promise<IDBPDatabase> | null = null;

  private db(): Promise<IDBPDatabase> {
    return (this.dbPromise ??= openDB(DB_NAME, 1, {
      upgrade(db) {
        if (!db.objectStoreNames.contains(STORE)) {
          db.createObjectStore(STORE, { keyPath: "id" });
        }
      },
    }));
  }

  async start(file: File, mediaKind: "image" | "document"): Promise<UploadHandle> {
    const { uploadId, signedUrl } = await this.deps.issueSignedUrl({
      filename: file.name,
      contentType: file.type,
      mediaKind,
    });
    const handle: PersistedHandle = {
      id: uploadId,
      filename: file.name,
      mediaKind,
      totalBytes: file.size,
      uploadedBytes: 0,
      status: "pending",
      signedUrl,
    };
    await this.persist(handle);
    return this.toHandle(handle);
  }

  async send(
    handle: UploadHandle,
    file: File,
    onProgress?: (bytes: number) => void,
  ): Promise<UploadHandle> {
    const persisted = await this.read(handle.id);
    if (!persisted) throw new Error(`No handle for ${handle.id}`);
    persisted.status = "uploading";
    await this.persist(persisted);
    try {
      // Lightweight PUT — real Uppy/S3 multipart is a follow-up.
      const response = await fetch(persisted.signedUrl, {
        method: "PUT",
        body: file,
        headers: { "Content-Type": file.type },
      });
      if (!response.ok) {
        throw new Error(`Upload failed: ${response.status}`);
      }
      persisted.uploadedBytes = file.size;
      persisted.status = "succeeded";
      await this.persist(persisted);
      onProgress?.(file.size);
      return this.toHandle(persisted);
    } catch (err) {
      persisted.status = "failed";
      persisted.error = err instanceof Error ? err.message : String(err);
      await this.persist(persisted);
      return this.toHandle(persisted);
    }
  }

  async list(): Promise<UploadHandle[]> {
    const db = await this.db();
    const all = await db.getAll(STORE);
    return (all as PersistedHandle[]).map((h) => this.toHandle(h));
  }

  async drop(id: string): Promise<void> {
    const db = await this.db();
    await db.delete(STORE, id);
  }

  private toHandle(p: PersistedHandle): UploadHandle {
    return {
      id: p.id,
      filename: p.filename,
      mediaKind: p.mediaKind,
      totalBytes: p.totalBytes,
      uploadedBytes: p.uploadedBytes,
      status: p.status,
      error: p.error,
    };
  }

  private async persist(handle: PersistedHandle): Promise<void> {
    const db = await this.db();
    await db.put(STORE, handle);
  }

  private async read(id: string): Promise<PersistedHandle | undefined> {
    const db = await this.db();
    return (await db.get(STORE, id)) as PersistedHandle | undefined;
  }
}
