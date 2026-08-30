#!/usr/bin/env python3
# -*- coding: utf-8 -*-
"""
音游谱面对音助手 chart_align.py
================================================================
功能：分析音频 → 检测 BPM / 首拍相位 / 节奏稳定性；解析常见谱面格式
（Milestone .mil / osu! .osu / Arcaea .aff / Phigros .json(小规格+RPE)），
把每个音符与"音频节拍网格"逐一对齐检查：全局偏移建议、离拍音符清单、
细分分布统计、变速段警告，并可一键修复（--fix-offset / --snap，先备份 .bak）。

对音约定（与 Milestone 引擎 BeatAlignEngine 一致）：
    音频起音时刻 ≈ 谱面音符时刻 + Offset
    因此 Offset = 起音 - 音符 的中位数；Offset 为 +Δ 表示音频比谱面晚 Δms。
  · .mil / .aff / Phigros json：修复 = 把 offset 字段设为 Δ（引擎画风）。
  · .osu（单时间轴）：修复 = 全部 HitObjects + TimingPoints 整体 +Δms。

依赖：numpy（必须）、av（推荐，解码 mp3/ogg/flac/wav）；
      未安装 av 时回退 miniaudio / 标准库 wave（仅 wav）。
安装：pip install numpy av

用法示例：
  python chart_align.py 谱面.osu --audio 歌曲.mp3
  python chart_align.py 谱面.mil --audio 歌曲.ogg --grid 4 --top 15
  python chart_align.py 谱面.osu --audio 歌曲.mp3 --fix-offset
  python chart_align.py 谱面.mil --audio 歌曲.wav --snap 4        # 吸附到 1/4 拍
  python chart_align.py --audio 歌曲.mp3                          # 只分析音频(BPM/首拍)
  python chart_align.py --selftest                                # 自检
"""

import argparse
import bisect
import json
import math
import os
import re
import shutil
import sys
import tempfile

import numpy as np

try:
    sys.stdout.reconfigure(encoding="utf-8", errors="replace")
except Exception:
    pass

# =====================================================================
# 1. 音频解码
# =====================================================================

def _decode_with_av(path):
    """PyAV（自带 FFmpeg）解码 → (float32 单声道, 采样率)。"""
    import av
    c = av.open(path)
    st = c.streams.audio[0]
    st.thread_type = "AUTO"
    frames = []
    for f in c.decode(st):
        arr = f.to_ndarray()               # (ch, n)
        if arr.dtype == np.int16:
            arr = arr.astype(np.float32) / 32768.0
        elif arr.dtype == np.int32:
            arr = arr.astype(np.float32) / 2147483648.0
        elif arr.dtype == np.uint8:
            arr = (arr.astype(np.float32) - 128.0) / 128.0
        elif arr.dtype in (np.float64,):
            arr = arr.astype(np.float32)
        frames.append(arr.astype(np.float32))
    if not frames:
        raise RuntimeError("无音频帧")
    a = np.concatenate(frames, axis=1)
    pcm = a.mean(axis=0) if a.shape[0] > 1 else a[0]
    return np.ascontiguousarray(pcm), int(st.rate or 44100)


def _decode_with_miniaudio(path):
    """miniaudio 回退。samples 可能是 (n,) 或 (n, ch)。"""
    import miniaudio
    s = miniaudio.decode_file(path)
    arr = np.ascontiguousarray(s.samples, dtype=np.float32)
    if arr.ndim == 2:
        pcm = arr.mean(axis=1) if arr.shape[1] > 1 else arr[:, 0]
    else:
        ch = getattr(s, "nchannels", 1) or 1
        if ch > 1 and arr.size % ch == 0:
            arr2 = arr.reshape(-1, ch)
            pcm = arr2.mean(axis=1)
        else:
            pcm = arr
    return np.ascontiguousarray(pcm), int(s.sample_rate)


def _decode_with_stdlib_wav(path):
    """标准库 wave 回退（仅 16-bit PCM WAV）。"""
    import wave
    with wave.open(path, "rb") as f:
        sr = f.getframerate()
        ch = f.getnchannels()
        sw = f.getsampwidth()
        raw = f.readframes(f.getnframes())
    if sw not in (1, 2, 4):
        raise RuntimeError("仅支持 8/16/32bit PCM WAV")
    if sw == 1:
        x = np.frombuffer(raw, dtype=np.uint8).astype(np.float32) - 128.0
        x /= 128.0
    elif sw == 2:
        x = np.frombuffer(raw, dtype="<i2").astype(np.float32) / 32768.0
    else:
        x = np.frombuffer(raw, dtype="<i4").astype(np.float32) / 2147483648.0
    if ch > 1:
        x = x.reshape(-1, ch).mean(axis=1)
    return np.ascontiguousarray(x), int(sr)


def decode_audio(path):
    """解码音频为 (float32 单声道, 采样率)。mp3/ogg/flac/wav/opus 均可（av）。"""
    if not os.path.isfile(path):
        raise RuntimeError("音频文件不存在: " + path)
    errs = []
    for fn in (_decode_with_av, _decode_with_miniaudio, _decode_with_stdlib_wav):
        try:
            return fn(path)
        except Exception as e:
            errs.append("%s: %s" % (getattr(fn, "__name__", "?"), e))
    raise RuntimeError("无法解码 %s（尝试 pip install av）\n  " + " | ".join(errs) % path
                      if False else
                      "无法解码 %s（尝试 pip install av）\n  %s" % (path, "\n  ".join(errs)))


def synth_click_wav(path, sr=44100.0, dur_s=7.0, bpm=120.0, offset_ms=40.0,
                    first_ms=1000.0, decay=0.014, freq=880.0):
    """合成 {bpm}BPM 快衰减脉冲音轨（自检/测试用，WAV 16-bit）。"""
    import wave
    n = int(sr * dur_s)
    pcm = np.zeros(n, dtype=np.float32)
    T = 60000.0 / bpm
    k = 0
    while True:
        beat = first_ms + offset_ms + k * T
        start = int(beat * sr / 1000.0)
        if start >= n:
            break
        m = int(sr * 2 / 1000.0)
        tt = np.arange(m, dtype=np.float32)
        pcm[start:start + m] += (0.9 * np.exp(-tt * decay)
                                 * np.sin(2 * np.pi * freq * tt / sr)).astype(np.float32)
        k += 1
    pcm = np.clip(pcm, -1.0, 1.0)
    with wave.open(path, "wb") as f:
        f.setnchannels(1)
        f.setsampwidth(2)
        f.setframerate(int(sr))
        f.writeframes((pcm * 32767.0).astype("<i2").tobytes())
    return int(sr), n


# =====================================================================
# 2. 起音/节奏 DSP（纯 numpy，无需 librosa）
# =====================================================================

# spectral_flux(frame=1024, hop=512) 的帧窗口中心偏移（帧数），起音峰时间校正用
FRAME_CENTER_FRAMES = 1024.0 / (2.0 * 512.0)

def spectral_flux(pcm, sr, frame=1024, hop=512):
    """Hanning STFT 幅值谱 → 正差分谱通量（起音包络）。返回 (env, raw_flux, frame_rate)。
    env = 平滑归一化包络（0..1）；raw_flux = 原始单帧通量（峰值定位用，亚帧细化）。"""
    n = len(pcm)
    if n < frame * 2:
        raise RuntimeError("音频过短（<%.2fs）" % (frame * 2 / float(sr)))
    win = np.hanning(frame).astype(np.float32)
    pad = (hop - (n - frame) % hop) % hop
    x = np.concatenate([pcm, np.zeros(pad, dtype=np.float32)])
    nf = 1 + (len(x) - frame) // hop
    env_parts = []
    flux_parts = []
    chunk = 8192
    prev = None
    for s0 in range(0, nf, chunk):
        e1 = min(s0 + chunk, nf)
        idx = np.arange(frame)[None, :] + hop * np.arange(s0, e1)[:, None]
        mag = np.abs(np.fft.rfft(x[idx] * win, axis=1))
        logm = np.log1p(mag)
        flux = np.maximum(0.0, np.diff(logm, axis=1)).sum(axis=1)  # (e1-s0-1)
        if prev is not None:
            flux = np.concatenate([[prev], flux])
        fpart = flux.astype(np.float64)
        flux_parts.append(fpart)
        if logm.shape[0] > 1:
            prev = float(np.maximum(0.0, logm[-1] - logm[-2]).sum())
        else:
            prev = None
    flux = np.concatenate(flux_parts)
    # 平滑 ~60ms 窗 → 包络
    k = max(1, int(round(0.060 * sr / hop)))
    env = np.convolve(flux, np.ones(k, dtype=np.float64) / k, mode="same")
    peak = float(np.percentile(env, 98.0))
    if peak <= 0:
        raise RuntimeError("音频静音，无起音信息")
    env = env / peak
    return env, flux, sr / hop


def onset_peaks(env, raw_flux, frame_rate, sep_ms=90.0, thresh_ratio=0.10,
                frame_center_frames=0.0):
    """起音峰（ms）。粗峰（平滑包络局部极大）→ 原始通量窗口取极大 + 抛物线亚帧细化。
    frame_center_frames：帧窗口中心偏移（spectral_flux 的 frame/(2*hop)），恒定时移校准。"""
    thr = float(env.mean() + thresh_ratio * (env.max() - env.mean()))
    n = len(env)
    cand = []
    for i in range(1, n - 1):
        v = env[i]
        if v >= thr and v >= env[i - 1] and v >= env[i + 1]:
            cand.append((v, i))
    cand.sort(reverse=True)
    sep = int(sep_ms / 1000.0 * frame_rate)
    taken = np.zeros(n, dtype=bool)
    peaks = []
    wn = 6
    for v, i in cand:
        lo, hi = max(0, i - sep), min(n, i + sep + 1)
        if taken[lo:hi].any():
            continue
        # 原始通量窗口极大（更尖锐 → 更准）
        j, dv = i, v
        wl, wh = max(0, i - wn), min(n, i + wn + 1)
        if raw_flux is not None and wh - wl > 2:
            wj = wl + int(np.argmax(raw_flux[wl:wh]))
            if raw_flux[wj] > 0:
                j = wj
                dv = raw_flux[j]
        # 抛物线细化
        d = 0.0
        src = raw_flux if raw_flux is not None else env
        if 0 < j < n - 1 and src[j - 1] >= 0 and src[j + 1] >= 0:
            a, b, c = src[j - 1], src[j], src[j + 1]
            denom = (a - 2 * b + c)
            if abs(denom) > 1e-12:
                d = (a - c) / (2.0 * denom)
                d = max(-1.0, min(1.0, d))
        peaks.append((j + d + frame_center_frames) * 1000.0 / frame_rate)
        taken[lo:hi] = True
        if len(peaks) >= 4000:
            break
    peaks.sort()
    return np.array(peaks, dtype=np.float64)


def estimate_tempo(env, frame_rate, bpm_min=60.0, bpm_max=300.0):
    """起音包络自相关 + 谐波和（h=1..4）→ BPM。返回 (bpm_raw, lag_frames)。
    亚帧细化同样在谐波和曲线上做（避免原始 ACF 峰位与谐波峰位不一致）。"""
    e = env - env.mean()
    n = len(e)
    if n < 32:
        return 0.0, 0
    full = np.concatenate([e, np.zeros(n)])
    spec = np.fft.rfft(full)
    acf = np.fft.irfft(spec * np.conj(spec))[:n]
    lag_min = max(1, int(round((60.0 / bpm_max) * frame_rate)))
    lag_max = min(n - 2, int(round((60.0 / bpm_min) * frame_rate)))
    if lag_max <= lag_min:
        return 0.0, 0
    hs = np.zeros(n)
    for h in (1, 2, 3, 4):
        ns = (n - 1) // h
        hs[:ns] += acf[:ns * h:h] / float(h)
    # 只取 [lag_min, lag_max] 内的整数点
    lo = np.zeros(lag_min)
    hi = np.zeros(n - lag_max - 1)
    hs_w = np.concatenate([lo, hs[lag_min:lag_max + 1], hi])
    best_lag = int(np.argmax(hs_w))
    best_score = float(hs_w[best_lag])
    # 抛物线细化（在谐波和曲线上）
    lag = float(best_lag)
    if 1 <= best_lag < n - 2:
        a, b, c = hs_w[best_lag - 1], hs_w[best_lag], hs_w[best_lag + 1]
        denom = (a - 2 * b + c)
        if abs(denom) > 1e-12:
            d = (a - c) / (2.0 * denom)
            d = max(-0.5, min(0.5, d))
            lag = best_lag + d
    bpm = 60.0 / (lag / frame_rate)
    return float(bpm), float(lag)


def comb_score(env, frame_rate, T_ms, phi_ms, active_mask):
    """节拍网格平均包络（密集包络插值，仅统计活跃段）。"""
    if T_ms <= 0:
        return 0.0
    t0 = phi_ms
    beat_ms = np.arange(t0, min(env.shape[0] * 1000.0 / frame_rate - 1, t0 + 600000.0 + T_ms), T_ms)
    if len(beat_ms) < 3:
        return 0.0
    bt = beat_ms / 1000.0 * frame_rate
    idx = np.clip(bt.astype(int), 0, len(env) - 1)
    vals = env[idx]
    act = active_mask[idx] if active_mask is not None else np.ones(len(beat_ms), dtype=bool)
    vals = vals[act]
    return float(vals.mean()) if len(vals) else 0.0


def pick_tempo_candidate(env, frame_rate, bpm_raw, declared_bpm=None,
                         bpm_min=40.0, bpm_max=300.0, onsets=None):
    """在 {raw, 2raw, raw/2, raw/4?}（范围 40..300）里梳状打分选最优：
    得分 = 拍上平均包络 × 起音覆盖率（落在拍上的起音比例）——覆盖率可消除
    倍频/分频歧义（2 倍拍网格只覆盖一半起音）。有声明 BPM 时作为候选参与。"""
    if bpm_raw <= 0:
        return 0.0
    cands = set()
    for k in (-2, -1, 0, 1, 2):
        b = bpm_raw * (2.0 ** k)
        if bpm_min <= b <= bpm_max:
            cands.add(round(b, 4))
    if declared_bpm and declared_bpm > 0 and bpm_min <= declared_bpm <= bpm_max:
        cands.add(round(declared_bpm, 4))
    if not cands:
        return bpm_raw
    # 活跃段掩码（1s 窗均值 > 5% 峰值）
    k1 = int(round(1.0 * frame_rate))
    local = np.convolve(env, np.ones(k1, dtype=np.float64) / k1, mode="same")
    active = local > 0.05 * max(local.max(), 1e-9)
    best_bpm, best_s = 0.0, -1.0
    for b in sorted(cands):
        T = 60000.0 / b
        # 粗相位 + 细相位扫描
        best_phi, best_sb = 0.0, -1.0
        for phi in np.linspace(0.0, T, 5)[:-1]:
            s = comb_score(env, frame_rate, T, phi, active)
            if s > best_sb:
                best_sb, best_phi = s, phi
        for phi in np.linspace(best_phi - T * 0.5, best_phi + T * 0.5, 13):
            s = comb_score(env, frame_rate, T, phi, active)
            if s > best_sb:
                best_sb, best_phi = s, phi
        # 起音覆盖率（±60ms 落在拍网格上的起音比例）
        cov = 1.0
        if onsets is not None and len(onsets) >= 3:
            ks = np.arange(0, int(env.shape[0] * 1000.0 / frame_rate / T) + 1)
            beats = best_phi + ks * T
            i = np.searchsorted(beats, onsets)
            dl = np.full(len(onsets), 1e18)
            dr = np.full(len(onsets), 1e18)
            m = i < len(beats)
            dl[m] = np.abs(beats[i[m]] - onsets[m])
            m = i > 0
            dr[m] = np.abs(beats[i[m] - 1] - onsets[m])
            cov = float((np.minimum(dl, dr) <= 60.0).mean())
        s = best_sb * (0.35 + 0.65 * cov)
        if s > best_s + 1e-9:
            best_s, best_bpm = s, b
    return best_bpm


def refine_grid_local(peaks, phi, T, win_s=8.0, hop_s=4.0, snap_ratio=0.35,
                      iters=2, min_points=30):
    """全局拍网格细化（防漂移）：分窗局部吸附+回归校正，再对全部 (k, t) 拼接回归。
    直接对大窗口全局回归会因初始 BPM 误差累积错吸附，分窗校正可避免。"""
    if len(peaks) < 8 or T <= 0:
        return phi, T, 0.0
    p = np.sort(peaks)
    for _ in range(iters):
        k0 = int(math.floor((p[0] + T * 0.4 - phi) / T))
        k1 = int(math.ceil((p[-1] - T * 0.4 - phi) / T))
        ks = np.arange(max(-1, k0), k1 + 2)
        beats = phi + ks * T
        i = np.searchsorted(p, beats)
        dl = np.full(len(beats), 1e18)
        dr = np.full(len(beats), 1e18)
        m = i < len(p)
        dl[m] = np.abs(p[i[m]] - beats[m])
        m = i > 0
        dr[m] = np.abs(p[i[m] - 1] - beats[m])
        dev = np.minimum(dl, dr)
        ok = dev <= T * snap_ratio
        pts_k = []
        pts_t = []
        win = win_s * 1000.0
        hop = hop_s * 1000.0
        c = p[0] - win * 0.5
        while c < p[-1]:
            sel = (ks >= c) & (ks < c + win) & ok
            if sel.sum() >= 5:
                kk = ks[sel].astype(np.float64)
                tt = beats[sel] + dev[sel]
                b = float(np.cov(kk, tt)[0, 1] / np.var(kk)) if np.var(kk) > 1e-9 else 0.0
                a = float(tt.mean() - b * kk.mean())
                # 只保留窗口内残差合理的点
                resid = tt - (a + b * kk)
                keep = np.abs(resid) <= max(12.0, T * 0.04)
                if keep.sum() >= 4:
                    pts_k.append(kk[keep])
                    pts_t.append(tt[keep])
            c += hop
        if not pts_k:
            break
        K = np.concatenate(pts_k)
        TT = np.concatenate(pts_t)
        if len(K) < min_points:
            break
        b = float(np.cov(K, TT)[0, 1] / np.var(K))
        a = float(TT.mean() - b * K.mean())
        if b <= 0 or abs(b - T) / T > 0.02:
            break
        T = b
        phi = a % T
    # 最终统计：拍吸附比例
    ks = np.arange(0, int((p[-1] + 3.0 * T - phi) / T) + 1)
    beats = phi + ks * T
    i = np.searchsorted(p, beats)
    dl = np.full(len(beats), 1e18)
    dr = np.full(len(beats), 1e18)
    m = i < len(p)
    dl[m] = np.abs(p[i[m]] - beats[m])
    m = i > 0
    dr[m] = np.abs(p[i[m] - 1] - beats[m])
    dev = np.minimum(dl, dr)
    ratio = float((dev <= T * snap_ratio).mean()) if len(dev) else 0.0
    return phi, T, ratio


def refine_grid(peaks, phi, T, snap_ratio=0.3, iters=2):
    """拍网格细化：把粗网格每拍吸附到最近起音峰（|偏差| ≤ T*snap_ratio），
    然后最小二乘拟合 t = a + b·k（Ellis 风格）→ 更高精度的 BPM/相位。
    返回 (phi_opt, T_opt, 吸附拍数/总拍数)。"""
    if len(peaks) < 8 or T <= 0:
        return phi, T, 0.0
    p = np.sort(peaks)
    for _ in range(iters):
        ks = np.arange(0, int((p[-1] + 3.0 * T - phi) / T) + 1)
        beats = phi + ks * T
        i = np.searchsorted(p, beats)
        dl = np.full(len(beats), 1e18)
        dr = np.full(len(beats), 1e18)
        m = i < len(p)
        dl[m] = np.abs(p[i[m]] - beats[m])
        m = i > 0
        dr[m] = np.abs(p[i[m] - 1] - beats[m])
        dev = np.minimum(dl, dr)
        ok = dev <= T * snap_ratio
        kk = ks[ok].astype(np.float64)
        tt = beats[ok] + dev[ok]
        if len(kk) < 8:
            break
        b = float(np.cov(kk, tt)[0, 1] / np.var(kk)) if np.var(kk) > 1e-9 else 0.0
        a = float(tt.mean() - b * kk.mean())
        if b <= 0 or abs(b - T) / T > 0.02:
            break
        T = b
        phi = a % T
    # 最终统计
    ks = np.arange(0, int((p[-1] + 3.0 * T - phi) / T) + 1)
    beats = phi + ks * T
    i = np.searchsorted(p, beats)
    dl = np.full(len(beats), 1e18)
    dr = np.full(len(beats), 1e18)
    m = i < len(p)
    dl[m] = np.abs(p[i[m]] - beats[m])
    m = i > 0
    dr[m] = np.abs(p[i[m] - 1] - beats[m])
    dev = np.minimum(dl, dr)
    ratio = float((dev <= T * snap_ratio).mean()) if len(dev) else 0.0
    return phi, T, ratio


def refine_phase(peaks, T_ms, env=None, frame_rate=None):
    """用起音峰密度（高斯核）扫描相位 → (phi_ms, 每拍对齐分)。"""
    if len(peaks) < 3 or T_ms <= 0:
        return 0.0, 0.0
    sigma = 15.0
    def score(phi, fine=True):
        # 计算 {phi + k*T} 各拍起音距离 → 高斯和
        first, last = peaks[0], peaks[-1]
        k0 = int(math.floor((first - phi) / T_ms))
        k1 = int(math.ceil((last - phi) / T_ms))
        ks = np.arange(max(0, k0), k1 + 1)
        beats = phi + ks * T_ms
        if len(beats) < 3:
            return 0.0
        i = np.searchsorted(peaks, beats)
        dl = np.full(len(beats), 1e9)
        dr = np.full(len(beats), 1e9)
        m = i < len(peaks)
        dl[m] = np.abs(peaks[i[m]] - beats[m])
        m = i > 0
        dr[m] = np.abs(peaks[i[m] - 1] - beats[m])
        d = np.minimum(dl, dr)
        score = float(np.exp(-(d ** 2) / (2.0 * sigma ** 2)).mean())
        return score
    best_phi, best_s = 0.0, -1.0
    for phi in np.arange(0.0, T_ms, 2.0):
        s = score(phi)
        if s > best_s:
            best_s, best_phi = s, phi
    for phi in np.arange(best_phi - 4.0, best_phi + 4.0 + 1e-9, 0.5):
        s = score(phi)
        if s > best_s:
            best_s, best_phi = s, phi
    return float(best_phi), float(best_s)


def tempo_drift(peaks, phi, T, win_s=8.0, hop_s=4.0):
    """分段测速（基于细化拍网格：每拍吸附最近起音后取区间中位数→BPM），
    检测变速段。返回 [(center_ms, bpm), ...]。"""
    if len(peaks) < 8 or T <= 0:
        return []
    p = np.sort(peaks)
    k0 = int(math.floor((p[0] + T * 0.4 - phi) / T))
    k1 = int(math.ceil((p[-1] - T * 0.4 - phi) / T))
    ks = np.arange(max(-1, k0), k1 + 2)
    beats = phi + ks * T
    i = np.searchsorted(p, beats)
    dl = np.full(len(beats), 1e18)
    dr = np.full(len(beats), 1e18)
    m = i < len(p)
    dl[m] = np.abs(p[i[m]] - beats[m])
    m = i > 0
    dr[m] = np.abs(p[i[m] - 1] - beats[m])
    dev = np.minimum(dl, dr)
    ok = dev <= T * 0.3
    snapped = beats[ok] + dev[ok]
    sk = ks[ok]
    if len(sk) < 10:
        return []
    out = []
    win = win_s * 1000.0
    hop = hop_s * 1000.0
    t_end = p[-1]
    c = 0.0
    while c < t_end:
        sel = (sk >= c) & (sk < c + win)
        if sel.sum() >= 5:
            iv = np.diff(np.sort(snapped[sel]))
            iv = iv[iv > T * 0.5]
            if len(iv) >= 3:
                med = float(np.median(iv))
                # 折叠到全局拍长附近（线性比率，非幂次 → 更准）
                f = T / med
                f = 2.0 ** round(math.log2(max(0.5, min(2.0, f)))) if f > 0 else 1.0
                med2 = med * f
                if 0.94 * T <= med2 <= 1.06 * T:
                    out.append((c + win / 2.0, 60000.0 / med2))
        c += hop
    return out


# =====================================================================
# 3. 谱面解析
# =====================================================================

def _num(d, k, default=None):
    v = d.get(k)
    if v is None or isinstance(v, bool):
        return default
    try:
        return float(v)
    except (TypeError, ValueError):
        return default


def _rpe_beat(v):
    """RPE 时间三元组 [i,n,d] = i + n/d 拍；数字则原样。"""
    if isinstance(v, list) and len(v) >= 2:
        i, n = float(v[0]), float(v[1])
        d = float(v[2]) if len(v) >= 3 and float(v[2]) != 0 else 1.0
        return i + n / d
    return float(v)


def _phigros_scale(root):
    """Phigros 小规格时间单位：>1e5=微秒(÷1000)，<1e4=秒(×1000)，否则毫秒。
    忽略 ≥1e8 哨兵值（Re:PhiEdit '事件延续到结束'）。"""
    maxv = 0.0

    def collect(o):
        nonlocal maxv
        if isinstance(o, dict):
            for v in o.values():
                collect(v)
        elif isinstance(o, list):
            for v in o:
                collect(v)
        elif isinstance(o, (int, float)) and 0 <= o < 1e8:
            if o > maxv:
                maxv = o
    collect(root)
    if maxv > 1e5:
        return 1.0 / 1000.0
    if maxv < 1e4:
        return 1000.0
    return 1.0


def parse_chart(path):
    """解析谱面 → dict。notes: [(t_ms, end_ms|None, col|None, label), ...]"""
    ext = os.path.splitext(path)[1].lower()
    with open(path, "r", encoding="utf-8-sig", errors="replace") as f:
        text = f.read()
    if ext == ".mil":
        return _parse_mil(path, text)
    if ext == ".osu":
        return _parse_osu(path, text)
    if ext == ".aff":
        return _parse_aff(path, text)
    if ext == ".json":
        try:
            root = json.loads(text)
        except Exception:
            return None
        if isinstance(root, dict) and ("judgeLineList" in root or "judgeLineGroup" in root):
            return _parse_phigros(path, root)
        if isinstance(root, dict) and "BPMList" in root:
            return _parse_phigros(path, root)
        return None
    return None


def _parse_mil(path, text):
    root = json.loads(text)
    notes = []
    for n in root.get("notes", []):
        t = _num(n, "t")
        if t is None:
            t = _num(n, "time")
        if t is None:
            continue
        e = _num(n, "e")
        if e is None:
            e = _num(n, "end")
        col = n.get("c")
        if col is None and "x" in n:
            col = n.get("x")
        notes.append((float(t), float(e) if e is not None else None, col, n.get("type")))
    notes.sort(key=lambda x: x[0])
    return {
        "path": path, "kind": "mil", "title": root.get("title", ""),
        "mode": root.get("mode", ""), "notes": notes,
        "declared_bpm": _num(root, "bpm"), "declared_offset": _num(root, "offset"),
        "audio_hint": root.get("audio") or None, "root": root,
    }


def _parse_osu(path, text):
    sec = {}
    cur = None
    for line in text.splitlines():
        line = line.strip()
        if line.startswith("[") and line.endswith("]"):
            cur = line[1:-1]
            sec[cur] = []
            continue
        if cur:
            sec[cur].append(line)
    mode = 0
    for line in sec.get("General", []):
        if line.lower().startswith("mode:"):
            mode = int(line.split(":", 1)[1].strip())
    tps = []
    for line in sec.get("TimingPoints", []):
        if not line or ":" not in line:
            continue
        p = line.split(",")
        if len(p) < 2:
            continue
        try:
            tm = float(p[0]); bl = float(p[1])
        except ValueError:
            continue
        uninherited = int(p[6]) == 1 if len(p) > 6 and p[6].strip() != "" else True
        if uninherited and bl > 0:
            tps.append((tm, 60000.0 / bl))
    tps.sort()
    notes = []
    for line in sec.get("HitObjects", []):
        p = line.split(",")
        if len(p) < 3:
            continue
        try:
            t = float(p[2]); typ = int(p[3])
        except ValueError:
            continue
        end = None
        if typ & 128 and len(p) > 5:
            pm = re.match(r"(\d+\.?\d*):", p[5])
            if pm:
                end = float(pm.group(1))
        notes.append((t, end, None, "hold" if typ & 128 else "tap"))
    notes.sort(key=lambda x: x[0])
    audio = None
    for line in sec.get("General", []):
        if line.lower().startswith("audiofilename:"):
            audio = line.split(":", 1)[1].strip()
            break
    title = ""
    for line in sec.get("Metadata", []):
        if line.lower().startswith("title:"):
            title = line.split(":", 1)[1].strip()
            break
    return {
        "path": path, "kind": "osu", "title": title, "mode": "osu" + str(mode),
        "notes": notes, "declared_bpm": tps[0][1] if tps else None,
        "declared_offset": tps[0][0] if tps else None,
        "bpm_segments": tps, "audio_hint": audio, "lines": text.splitlines(keepends=True),
    }


def _parse_aff(path, text):
    lines = text.splitlines()
    offset = 0.0
    for line in lines:
        m = re.match(r"\s*AudioOffset:\s*([+-]?\d+)", line)
        if m:
            offset = float(m.group(1))
            break
    tps = []
    for line in lines:
        m = re.match(r"\s*timing\s*\(\s*([\d.]+)\s*,\s*([\d.]+)", line)
        if m:
            tps.append((float(m.group(1)), float(m.group(2))))
    tps.sort()
    notes = []
    for line in lines:
        line = line.strip()
        m = re.match(r"\((\d+\.?\d*),\s*(\d+\.?\d*),\s*(\d+\.?\d*)\)\s*;?", line)
        if m:
            notes.append((float(m.group(1)), None, float(m.group(2)), "tap"))
            continue
        m = re.match(r"hold\s*\(\s*(\d+\.?\d*)\s*,\s*(\d+\.?\d*)\s*,\s*(\d+\.?\d*)\s*\)", line)
        if m:
            t = float(m.group(1))
            notes.append((t, t + float(m.group(3)), float(m.group(2)), "hold"))
            continue
        m = re.match(r"arc\s*\(\s*(\d+\.?\d*)\s*,.*?,\s*(\d+\.?\d*)\s*\)", line)
        if m:
            t = float(m.group(1))
            notes.append((t, float(m.group(2)), None, "arc"))
            continue
        if re.match(r"flick\s*\(", line):
            m = re.match(r"flick\s*\(\s*(\d+\.?\d*)\s*,\s*(\d+\.?\d*)", line)
            if m:
                notes.append((float(m.group(1)), None, float(m.group(2)), "flick"))
    notes.sort(key=lambda x: x[0])
    return {
        "path": path, "kind": "aff", "title": os.path.basename(path),
        "mode": "arcaea", "notes": notes, "declared_bpm": tps[0][1] if tps else None,
        "declared_offset": offset, "bpm_segments": tps, "audio_hint": None,
    }


def _parse_phigros(path, root):
    is_rpe = "BPMList" in root
    notes = []
    declared_bpm = None
    declared_offset = None
    audio_hint = None
    if is_rpe:
        # BPM 分段（拍 → 毫秒 积分）
        bpmkfs = []
        for it in root.get("BPMList", []):
            if isinstance(it, dict):
                beat = _rpe_beat(it.get("startTime"))
                bpm = _num(it, "bpm", 120.0)
                bpmkfs.append((beat, bpm))
        if not bpmkfs:
            bpmkfs.append((0.0, 120.0))
        bpmkfs.sort()
        declared_bpm = bpmkfs[0][1]

        def beat_ms(beat):
            if len(bpmkfs) == 1:
                return beat * 60000.0 / bpmkfs[0][1]
            t = 0.0
            k = 0
            while k < len(bpmkfs) - 2 and bpmkfs[k + 1][0] <= beat:
                k += 1
            for i in range(k):
                t += (bpmkfs[i + 1][0] - bpmkfs[i][0]) * 60000.0 / bpmkfs[i][1]
            t += (beat - bpmkfs[k][0]) * 60000.0 / bpmkfs[k][1]
            return t

        lines = root.get("judgeLineList", [])
        for li in lines:
            for n in li.get("notes", []):
                st = beat_ms(_rpe_beat(n.get("startTime")))
                en = beat_ms(_rpe_beat(n.get("endTime")))
                if en < st:
                    en = st
                typ = int(round(_num(n, "type", 1.0)))
                notes.append((st, en, None,
                              {1: "tap", 2: "drag", 3: "hold", 4: "flick"}.get(typ, "tap")))
        meta = root.get("META", {})
        if isinstance(meta, dict):
            declared_offset = _num(meta, "offset")
            audio_hint = meta.get("song")
            title = meta.get("name") or meta.get("title") or path
        else:
            title = path
        title = title or path
        tmode = "phigros-rpe"
    else:
        scale = _phigros_scale(root)
        lines = root.get("judgeLineList", [])
        for li, line in enumerate(lines):
            bpm = _num(line, "bpm")
            if declared_bpm is None and bpm:
                declared_bpm = bpm
            for key in ("notesAbove", "notesBelow"):
                for n in line.get(key, []):
                    t = _num(n, "time")
                    if t is None:
                        continue
                    t *= scale
                    ht = _num(n, "holdTime", 0.0) * scale
                    typ = int(round(_num(n, "type", 1.0)))
                    notes.append((t, t + ht if ht > 0 else None, None,
                                  {1: "tap", 2: "drag", 3: "hold", 4: "flick"}.get(typ, "tap")))
        declared_offset = _num(root, "offset")
        title = root.get("title") or os.path.basename(path)
        tmode = "phigros-minor"
    notes.sort(key=lambda x: x[0])
    return {
        "path": path, "kind": "phigros", "sub": tmode, "title": title,
        "mode": "phigros", "notes": notes, "declared_bpm": declared_bpm,
        "declared_offset": declared_offset, "audio_hint": audio_hint, "root": root,
    }


def resolve_audio(chart, audio_arg=None):
    """确定音频路径：--audio ＞ 谱面字段 ＞ 同目录同名/首音。"""
    if audio_arg and os.path.isfile(audio_arg):
        return audio_arg
    d = os.path.dirname(os.path.abspath(chart["path"]))
    cands = []
    hint = chart.get("audio_hint")
    if hint:
        cands.append(hint if os.path.isabs(hint) else os.path.join(d, hint))
        base = os.path.splitext(os.path.basename(hint))[0]
        cands.append(os.path.join(d, base + os.path.splitext(os.path.basename(chart["path"]))[1]))
    stem = os.path.splitext(os.path.basename(chart["path"]))[0]
    for suf in ("_EZ", "_HD", "_IN", "_AT", "_Legacy", "_Easy", "_Normal", "_Hard"):
        if stem.endswith(suf):
            stem = stem[: -len(suf)]
            break
    for ext in (".mp3", ".ogg", ".wav", ".flac", ".opus", ".m4a", ".aac", ".wma"):
        cands.append(os.path.join(d, stem + ext))
    for ext in (".mp3", ".ogg", ".wav", ".flac"):
        cands.append(os.path.join(d, "audio" + ext))
    for c in cands:
        if c and os.path.isfile(c):
            return c
    return None


# =====================================================================
# 4. 对音分析
# =====================================================================

def offset_scan(notes_ms, onsets_ms, search=150.0, sigma=35.0, max_dist=250.0,
                match_window=60.0, offbeat_thr=40.0):
    """起音互相关找最优偏移 Δ（引擎同款：高斯核打分 + 粗扫 + 0.5ms 细化）。
    返回 dict: delta, matched/total, confidence, offbeat_notes[(t,dev)]"""
    res = {"delta": 0.0, "matched": 0, "total": 0, "confidence": 0.0,
           "offbeat": [], "scores": {}}
    if len(notes_ms) == 0 or len(onsets_ms) < 3:
        return res
    notes = np.array(notes_ms, dtype=np.float64)
    onsets = np.sort(onsets_ms)

    def score(delta):
        t = notes + delta
        i = np.searchsorted(onsets, t)
        dl = np.full(len(t), 1e18)
        dr = np.full(len(t), 1e18)
        m = i < len(onsets)
        dl[m] = np.abs(onsets[i[m]] - t[m])
        m = i > 0
        dr[m] = np.abs(onsets[i[m] - 1] - t[m])
        d = np.minimum(dl, dr)
        s = np.exp(-(d ** 2) / (2.0 * sigma ** 2))
        s[d > max_dist] = 0.0
        return float(s.sum())

    cands = np.arange(-search, search + 1e-9, 2.0)
    scores = np.array([score(d) for d in cands])
    bi = int(np.argmax(scores))
    best_d, best_s = float(cands[bi]), float(scores[bi])
    for d in np.arange(best_d - 2.0, best_d + 2.0 + 1e-9, 0.5):
        s = score(d)
        if s > best_s + 1e-12:
            best_s, best_d = s, d
    res["delta"] = round(best_d * 2) / 2.0
    res["scores"] = {round(float(d), 1): float(s) for d, s in zip(cands, scores)}

    # 命中统计 + 离拍
    t = notes + res["delta"]
    i = np.searchsorted(onsets, t)
    dl = np.full(len(t), 1e18)
    dr = np.full(len(t), 1e18)
    m = i < len(onsets)
    dl[m] = np.abs(onsets[i[m]] - t[m])
    m = i > 0
    dr[m] = np.abs(onsets[i[m] - 1] - t[m])
    d = np.minimum(dl, dr)
    res["matched"] = int((d <= match_window).sum())
    res["total"] = int(len(notes))
    res["confidence"] = res["matched"] / max(1, len(notes))
    offb = []
    for j in range(len(notes)):
        if d[j] > offbeat_thr:
            offb.append((float(notes[j]), float(d[j]) if d[j] < max_dist else 9999.0))
    res["offbeat"] = sorted(offb)[:5000]
    return res


def nearest_grid(t_ms, phi, T, sub):
    """t 在网格 {phi + k*T/sub} 上的最近点与偏差。返回 (tick_time, dev_ms, beat_pos)。"""
    b = (t_ms - phi) / T
    tick = round(b * sub) / sub
    return phi + tick * T, t_ms - (phi + tick * T), b


def subdivision_of(b, sub=4):
    """按细分归类：'1' 整拍, '2' 半拍, '4' 1/4, '8' 1/8, '12' 1/12, 'other'。"""
    for s in (1, 2, 4, 8, 12):
        v = round(b * s)
        if abs(b * s - v) < 0.02:
            return str(s)
    return "other"


def analyze_grid(notes, phi, T, delta, sub=4, tol_ms=25.0, active=None):
    """逐音符对网格偏差。notes: [(t, end, col, label)]。返回行+统计。"""
    rows = []
    for t, end, col, label in notes:
        tick_t, dev, b = nearest_grid(t + delta, phi, T, sub)
        subn = subdivision_of(b)
        rows.append({
            "t": t, "end": end, "col": col, "label": label, "b": b,
            "dev_ms": dev, "dev_beats": dev / T if T else 0.0,
            "sub": subn, "suggest": tick_t - delta,
        })
    devs = np.array([abs(r["dev_ms"]) for r in rows], dtype=np.float64)
    stats = {}
    if len(devs):
        stats["mean"] = float(devs.mean())
        stats["median"] = float(np.median(devs))
        stats["std"] = float(devs.std())
        stats["p95"] = float(np.percentile(devs, 95))
        stats["p99"] = float(np.percentile(devs, 99))
        stats["max"] = float(devs.max())
        stats["le10"] = float((devs <= 10).mean())
        stats["le25"] = float((devs <= 25).mean())
        stats["le40"] = float((devs <= 40).mean())
    else:
        stats = {k: 0.0 for k in ("mean", "median", "std", "p95", "p99", "max",
                                  "le10", "le25", "le40")}
    hist = {}
    for r in rows:
        hist[r["sub"]] = hist.get(r["sub"], 0) + 1
    n = len(rows)
    hist = {k: v / max(1, n) for k, v in hist.items()}
    rows.sort(key=lambda r: -abs(r["dev_ms"]))
    return rows, stats, hist


def chart_grid_dev(notes, segments, offset, sub=4):
    """音符相对谱面声明网格的偏差（自成对音/吸附质量；segments=[(time,bpm)]）。"""
    devs = []
    counts = {}
    for t, end, col, label in notes:
        if segments:
            i = bisect.bisect_right([s[0] for s in segments], t) - 1
            i = max(0, min(i, len(segments) - 1))
            T = 60000.0 / segments[i][1]
            phi = segments[i][0] if segments[i][0] > 0 else 0.0
        else:
            T = None
            phi = None
        if T is None or offset is None:
            b = None
        else:
            b = (t - phi) / T
            dev = t - (phi + round(b * sub) / sub * T)
            devs.append(dev)
            counts[subdivision_of(b)] = counts.get(subdivision_of(b), 0) + 1
    if devs:
        devs = np.array(devs)
        return {"mean": float(np.abs(devs).mean()), "p95": float(np.percentile(np.abs(devs), 95)),
                "le5ms": float((np.abs(devs) <= 5).mean()), "n": len(devs),
                "hist": {k: v / len(devs) for k, v in counts.items()}}
    return None


# =====================================================================
# 5. 报告
# =====================================================================

def _fmt(t):
    return ("%.1f" % t).rstrip("0").rstrip(".") if t is not None else "-"


def _bpm_err(decl, det):
    if not decl or not det:
        return None
    return abs(decl - det) / det * 100.0


def render_report(rep, opts):
    """rep: dict（见 main 组装）。返回字符串报告。"""
    L = []
    W = 78
    L.append("=" * W)
    L.append(" 音游谱面对音助手 · 报告")
    L.append("=" * W)

    a = rep.get("audio")
    if a:
        L.append("▶ 音频：%s（%.1fs · %dHz · 起音峰 %d）"
                 % (os.path.basename(a["path"]), a["dur"], a["sr"], len(a["onsets"])))
        L.append("  检测 BPM：%s（拍长 %s ms）· 首拍时刻 %s ms"
                 % (_fmt(a["bpm"]), _fmt(a["T"]), _fmt(a["phi"])))
        L.append("  起音命中：%.2f（拍网格对齐到起音峰的比例）" % a["hit_ratio"])
        if a["bpm"] and a["T"]:
            drift = a.get("drift", [])
            if len(drift) >= 2:
                bpms = [x[1] for x in drift]
                dmax = max(bpms) - min(bpms)
                dmaxp = dmax / max(1e-9, a["bpm"]) * 100.0
                if dmaxp > 2.5:
                    L.append("  ⚠ 变速迹象：BPM 波动 %.1f%%（%.1f–%.1f）→ 请分段检查或人工校速"
                             % (dmaxp, min(bpms), max(bpms)))
                    for cms, b in drift:
                        L.append("     %8.1f s → %s BPM" % (cms / 1000.0, _fmt(b)))
                else:
                    L.append("  变速检查：BPM 波动 %.1f%%（%.1f–%.1f）→ 节奏稳定"
                             % (dmaxp, min(bpms), max(bpms)))
        L.append("")

    for ch in rep["charts"]:
        L.append("▶ 谱面：%s（%s%s · %s）"
                 % (os.path.basename(ch["path"]),
                    {"mil": "Milestone .mil", "osu": "osu!mania/std",
                     "aff": "Arcaea .aff", "phigros": "Phigros"}.get(ch["kind"], ch["kind"]),
                    " RPE" if ch["kind"] == "phigros" and ch.get("sub") == "phigros-rpe" else "",
                    (ch.get("title") or "")[:40]))
        notes = ch["notes"]
        L.append("  音符：%d（含 hold/drag/flick）· 声明 BPM %s · 声明偏移 %s ms"
                 % (len(notes), _fmt(ch["declared_bpm"]), _fmt(ch["declared_offset"])))

        if a:
            err = _bpm_err(ch["declared_bpm"], a["bpm"])
            if err is not None:
                tag = "一致" if err <= 1.5 else ("近似" if err <= 4 else "⚠ 明显不符")
                L.append("  BPM 对比：谱面 %s vs 音频 %s（差 %.1f%%）→ %s"
                         % (_fmt(ch["declared_bpm"]), _fmt(a["bpm"]), err, tag))
            if ch["declared_offset"] is not None and a["phi"] is not None:
                phidiff = ch["declared_offset"] - a["phi"]
                L.append("  相位对比：谱面首拍 %s ms vs 音频 %s ms（差 %s ms）"
                         % (_fmt(ch["declared_offset"]), _fmt(a["phi"]), _fmt(phidiff)))

        if ch.get("scan"):
            s = ch["scan"]
            L.append("  ── 对音（音频起音互相关，引擎约定：音频 ≈ 音符 + 偏移）──")
            L.append("  建议偏移 Δ：%s ms（命中 %d/%d · 置信 %.0f%%）"
                     % (_fmt(s["delta"]), s["matched"], s["total"], s["confidence"] * 100))
            if a and abs(s["delta"]) >= 30:
                L.append("    ⚠ Δ 较大：整体音符相对音频偏移明显，人工听一遍确认后可用 --fix-offset 应用")
        if a and ch.get("grid"):
            g = ch["grid"]; st = g["stats"]
            L.append("  ── 网格偏差（对齐音频网格 · 细分 1/%d · 容差 %dms）──" % (opts.grid, opts.tol))
            L.append("  偏差：均值 %s ms · 中位 %s ms · σ %s ms · p95 %s ms"
                     % (_fmt(st["mean"]), _fmt(st["median"]), _fmt(st["std"]), _fmt(st["p95"])))
            L.append("  容差内：|≤10ms| %.0f%% · |≤25ms| %.0f%% · |≤40ms| %.0f%%"
                     % (st["le10"] * 100, st["le25"] * 100, st["le40"] * 100))
            h = g["hist"]
            L.append("  细分分布：" + " · ".join(
                ("整拍" if k == "1" else "1/%s" % k) + " %.0f%%" % (h.get(k, 0) * 100)
                for k in ("1", "2", "4", "8", "12", "other")))
            if opts.top > 0 and len(g["rows"]):
                L.append("  ── 最差 %d 个音符（按网格偏差降序；建议 = 吸附到最近网格后的时间）──" % opts.top)
                L.append("   %8s  %7s  %6s  %8s  %7s  %s"
                         % ("时间ms", "偏差ms", "垫数", "拍位", "细分", "吸附建议"))
                for r in g["rows"][:opts.top]:
                    L.append("   %8s  %7s  %6s  %8s  %7s  %s（Δ %s ms）"
                             % (_fmt(r["t"]), _fmt(abs(r["dev_ms"])),
                                _fmt(r["col"]), _fmt(r["b"]), r["sub"],
                                _fmt(r["suggest"]), _fmt(r["suggest"] - r["t"])))
            if ch.get("chart_grid"):
                cg = ch["chart_grid"]
                L.append("  谱面网格（声明 BPM/偏移）：± %.1f ms · p95 %.1f ms · ≤5ms %.0f%%"
                         % (cg["mean"], cg["p95"], cg["le5ms"] * 100))
        if ch.get("fix_note"):
            L.append("  " + ch["fix_note"])
        L.append("")

    # 修复动作
    for m in rep.get("actions", []):
        L.append("✎ " + m)
    if "snap_note" in rep:
        L.append("✎ " + rep["snap_note"])
    if rep.get("verdicts"):
        L.append("-" * W)
        for v in rep["verdicts"]:
            L.append(v)
    return "\n".join(L)


# =====================================================================
# 6. 写回修复
# =====================================================================

def _backup(path):
    bak = path + ".bak"
    shutil.copy2(path, bak)
    return bak


def apply_mil_fix(chart, delta, invert=False, snap=None, grid=None):
    """引擎约定：offset 字段 = Δ（音频 ≈ 音符 + offset）。"""
    root = chart["root"]
    d = -delta if invert else delta
    root["offset"] = round(d, 3)
    if snap:
        if not grid:
            return "（%s：无音频网格，跳过吸附）" % os.path.basename(chart["path"])
        phi, T, sub, delta = grid
        for n in root.get("notes", []):
            t = _num(n, "t")
            if t is None:
                continue
            t_audio = t + delta
            tick = phi + round((t_audio - phi) / T * sub) / sub * T
            n["t"] = round(tick - delta, 3)
    bak = _backup(chart["path"])
    with open(chart["path"], "w", encoding="utf-8") as f:
        json.dump(root, f, ensure_ascii=False, indent=2)
        f.write("\n")
    return "已写回 %s（备份 %s）offset → %sms%s" % (
        os.path.basename(chart["path"]), os.path.basename(bak), _fmt(d),
        "；音符已吸附 1/%d" % snap if snap else "")


def apply_osu_fix(chart, delta, invert=False, snap=None, grid=None):
    """单时间轴：所有对象 + TimingPoints 整体平移。"""
    d = -delta if invert else delta
    lines = chart["lines"]
    new_lines = []
    in_tp = in_ho = False
    if snap and not grid:
        return "（%s：无音频网格，跳过吸附）" % os.path.basename(chart["path"])
    for line in lines:
        s = line.strip()
        if s.startswith("[TimingPoints]"):
            in_tp, in_ho = True, False
            new_lines.append(line)
            continue
        if s.startswith("[HitObjects]"):
            in_ho, in_tp = True, False
            new_lines.append(line)
            continue
        if s.startswith("["):
            in_tp = in_ho = False
        if in_tp:
            p = line.split(",")
            if len(p) >= 2:
                try:
                    tm = float(p[0])
                    p[0] = _fmt(tm + d)
                    line = ",".join(p)
                except ValueError:
                    pass
        elif in_ho:
            p = line.split(",")
            if len(p) >= 3:
                try:
                    tm = float(p[2])
                    new_t = tm + d
                    if snap and grid:
                        phi, T, sub, delta = grid
                        t_audio = new_t
                        tick = phi + round((t_audio - phi) / T * sub) / sub * T
                        if abs(new_t - tick) <= snap_tol_wrapper(grid, snap, new_t):
                            new_t = tick
                    p[2] = _fmt(new_t)
                    typ = int(p[3]) if len(p) > 3 else 0
                    if typ & 128 and len(p) > 5:
                        m = re.match(r"(\d+\.?\d*):", p[5])
                        if m:
                            end = float(m.group(1))
                            p[5] = ("%s:" % _fmt(end + (new_t - tm))) + p[5][m.end():]
                    line = ",".join(p)
                except (ValueError, IndexError):
                    pass
        new_lines.append(line)
    bak = _backup(chart["path"])
    with open(chart["path"], "w", encoding="utf-8", newline="") as f:
        f.writelines(new_lines)
    msg = "已写回 %s（备份 %s）对象+TimingPoints %s%s ms" % (
        os.path.basename(chart["path"]), os.path.basename(bak),
        "全部平移" , _fmt(d))
    if snap:
        msg += "；音符已吸附 1/%d" % snap
    return msg


def snap_tol_wrapper(grid, sub, t):
    return max(20.0, 60000.0 / 300.0 * 0.25)


def apply_aff_fix(chart, delta, invert=False, snap=None, grid=None):
    d = -delta if invert else delta
    path = chart["path"]
    with open(path, "r", encoding="utf-8-sig") as f:
        text = f.read()
    new = re.sub(r"(?m)^\s*AudioOffset:\s*([+-]?\d+)",
                 lambda m: "AudioOffset:" + _fmt(d), text, count=1)
    if snap and grid:
        phi, T, sub, delta = grid
        def shift_note(m):
            t = float(m.group(1))
            tick = phi + round((t - phi) / T * sub) / sub * T
            return "(%s" % _fmt(tick)
        new = re.sub(r"\((\d+\.?\d*)", shift_note, new)
    bak = _backup(path)
    with open(path, "w", encoding="utf-8", newline="") as f:
        f.write(new)
    return "已写回 %s（备份 %s）AudioOffset → %sms%s" % (
        os.path.basename(path), os.path.basename(bak), _fmt(d),
        "；音符已吸附 1/%d" % snap if snap else "")


def apply_phigros_fix(chart, delta, invert=False, snap=None, grid=None):
    d = -delta if invert else delta
    root = chart["root"]
    sub_kind = chart.get("sub")
    target = root.get("META") if sub_kind == "phigros-rpe" and isinstance(root.get("META"), dict) else root
    if sub_kind == "phigros-rpe":
        target["offset"] = round(d, 3)
    else:
        target["offset"] = round(d, 3)
    bak = _backup(chart["path"])
    with open(chart["path"], "w", encoding="utf-8") as f:
        json.dump(root, f, ensure_ascii=False, indent=2)
        f.write("\n")
    return "已写回 %s（备份 %s）offset → %sms%s" % (
        os.path.basename(chart["path"]), os.path.basename(bak), _fmt(d),
        "（RPE/META 或根字段；吸附请用编辑器）" if sub_kind == "phigros-rpe" else "")


# =====================================================================
# 7. 自检
# =====================================================================

def _selftest_case(bpm, offset_ms, jitter=None, dur_s=7.0, expect_bpm=None,
                   expect_delta=None, tol_bpm=4.0, tol_delta=6.0):
    d = tempfile.mkdtemp(prefix="align_selftest_")
    wav = os.path.join(d, "tone.wav")
    sr, n = synth_click_wav(wav, sr=44100.0, dur_s=dur_s, bpm=bpm, offset_ms=offset_ms)
    pcm, sr2 = decode_audio(wav)
    env, raw, fr = spectral_flux(pcm, sr2)
    peaks = onset_peaks(env, raw, fr, frame_center_frames=FRAME_CENTER_FRAMES)
    bpm_raw, _ = estimate_tempo(env, fr)
    bpm_used = pick_tempo_candidate(env, fr, bpm_raw, expect_bpm or bpm, onsets=peaks)
    T = 60000.0 / bpm_used
    phi, ph_conf = refine_phase(peaks, T)
    phi, T, snapr = refine_grid_local(peaks, phi, T)
    note_ms = [1000.0 + k * 60000.0 / bpm for k in range(int(dur_s * bpm / 60.0) - 1)]
    if jitter:
        rng = np.random.default_rng(7)
        note_ms = [t + float(rng.normal(0, jitter)) for t in note_ms]
    scan = offset_scan(note_ms, peaks)
    ok_bpm = abs(60000.0 / T - (expect_bpm or bpm)) <= tol_bpm
    ok_delta = abs(scan["delta"] - (expect_delta if expect_delta is not None else offset_ms)) <= tol_delta
    ok_conf = scan["confidence"] >= 0.85
    return (ok_bpm and ok_delta and ok_conf,
            "BPM %.1f(期望%.1f) · Δ %.1fms(期望%.1f) · 命中 %.0f%% · 首拍 %.1fms"
            % (60000.0 / T, expect_bpm or bpm, scan["delta"],
               expect_delta if expect_delta is not None else offset_ms,
               scan["confidence"] * 100, phi))


def selftest():
    cases = [
        (120.0, 40.0, None, "120BPM+40ms"),
        (174.5, 12.0, None, "174.5BPM+12ms"),
        (120.0, -30.0, None, "120BPM-30ms"),
        (120.0, 40.0, 0.0, None),
    ]
    all_ok = True
    for bpm, off, jit, name in cases:
        try:
            ok, msg = _selftest_case(bpm, off, jit, expect_delta=off)
        except Exception as ex:
            ok, msg = False, "异常: %s" % ex
        all_ok = all_ok and ok
        print("%s [%s] %s" % ("PASS" if ok else "FAIL", name, msg))
    # 解析/写回往返
    d = tempfile.mkdtemp(prefix="align_selftest2_")
    import copy
    mil = os.path.join(d, "t.mil")
    sr, n = synth_click_wav(os.path.join(d, "t.wav"), dur_s=7.0, bpm=120.0, offset_ms=40.0)
    root = {"format": "milestone-1", "mode": "mania", "title": "自检", "bpm": 120,
            "offset": 0, "audio": "t.wav", "keys": 4,
            "notes": [{"t": 1000.0 + k * 500.0, "c": k % 4} for k in range(13)]}
    with open(mil, "w", encoding="utf-8") as f:
        json.dump(root, f, ensure_ascii=False, indent=2)
    ch = parse_chart(mil)
    pcm, sr2 = decode_audio(os.path.join(d, "t.wav"))
    env, raw, fr = spectral_flux(pcm, sr2)
    peaks = onset_peaks(env, raw, fr, frame_center_frames=FRAME_CENTER_FRAMES)
    scan = offset_scan([n0[0] for n0 in ch["notes"]], peaks)
    msg = apply_mil_fix(ch, scan["delta"])
    root2 = json.load(open(mil, encoding="utf-8"))
    ok = abs(root2["offset"] - scan["delta"]) <= 1.5 and os.path.exists(mil + ".bak")
    all_ok = all_ok and ok
    print("%s [mil 解析+写回] 建议Δ=%.1fms → offset=%.1fms；%s"
          % ("PASS" if ok else "FAIL", scan["delta"], root2["offset"], msg))
    return 0 if all_ok else 1


# =====================================================================
# 8. main
# =====================================================================

def main(argv=None):
    ap = argparse.ArgumentParser(
        description="音游谱面对音助手：BPM/相位检测 + 音符对网格诊断 + 偏移/吸附修复",
        formatter_class=argparse.RawDescriptionHelpFormatter)
    ap.add_argument("charts", nargs="*", help="谱面文件（.mil/.osu/.aff/.json）；省略则只分析音频")
    ap.add_argument("--audio", help="音频文件（缺省按谱面/同目录推断）")
    ap.add_argument("--bpm", type=float, default=None, help="手动指定 BPM（替代检测）")
    ap.add_argument("--offset-ms", type=float, default=None, help="手动指定首拍时刻（音频内 ms）")
    ap.add_argument("--grid", type=int, default=4, help="网格细分：1/2/4/8/16（默认 4）")
    ap.add_argument("--tol", type=float, default=25.0, help="容差 ms（默认 25）")
    ap.add_argument("--top", type=int, default=10, help="最差音符列表条数（默认 10, 0=不列）")
    ap.add_argument("--fix-offset", action="store_true", help="把建议偏移写回谱面（自动 .bak）")
    ap.add_argument("--snap", type=int, default=0, help="把音符吸附到音频网格 1/N 拍（0=关闭）")
    ap.add_argument("--invert", action="store_true", help="写回时取反符号（编辑器语义相反时）")
    ap.add_argument("--json", dest="json_out", help="导出 JSON 报告路径")
    ap.add_argument("--selftest", action="store_true", help="运行自检")
    ap.add_argument("-q", "--quiet", action="store_true", help="只输出结论行")
    opts = ap.parse_args(argv)

    if opts.selftest:
        return selftest()

    rep = {"charts": []}
    nevers = []

    # 音频分析
    audio_path = opts.audio
    if not audio_path:
        for c in opts.charts:
            ch_file = parse_chart(c)
            if ch_file:
                audio_path = resolve_audio(ch_file)
                if audio_path:
                    break
                if ch_file.get("audio_hint"):
                    nevers.append("%s 音频 %s 缺失" % (os.path.basename(c), ch_file["audio_hint"]))
    if audio_path:
        rep = analyze_audio_node(audio_path, opts, rep)

    for cpath in opts.charts:
        ch = parse_chart(cpath)
        if not ch:
            print("跳过无法识别的谱面：%s" % cpath)
            continue
        node = {
            "path": cpath, "kind": ch["kind"], "sub": ch.get("sub"),
            "title": ch.get("title", ""), "notes": ch["notes"],
            "declared_bpm": ch.get("declared_bpm"), "declared_offset": ch.get("declared_offset"),
            "bpm_segments": ch.get("bpm_segments"),
        }
        if rep.get("audio_node"):
            a = rep["audio_node"]
            # 起音互相关
            node["scan"] = offset_scan([n[0] for n in ch["notes"]], a["onsets"], offbeat_thr=opts.tol)
            # 网格偏差
            phi, T, delta = a["phi"], a["T"], node["scan"]["delta"]
            if T and T > 0 and ch["notes"]:
                rows, stats, hist = analyze_grid(ch["notes"], phi, T, delta,
                                                 sub=opts.grid, tol_ms=opts.tol)
                node["grid"] = {"rows": rows, "stats": stats, "hist": hist}
                # hold 尾检查
                holds_end = []
                for t, end, col, label in ch["notes"]:
                    if end is not None:
                        holds_end.append(end)
                if holds_end:
                    ends_dev = [abs(nearest_grid(e + delta, phi, T, opts.grid)[1]) for e in holds_end]
                    node["hold_end_dev"] = float(np.mean(ends_dev))
            node["chart_grid"] = chart_grid_dev(ch["notes"], node.get("bpm_segments"),
                                                ch.get("declared_offset"), opts.grid)
        rep["charts"].append(node)

    rep["verdicts"] = build_verdicts(rep, opts)
    rep["actions"] = build_actions(rep, opts, audio_path)

    report = render_report(rep, opts)
    print(report if not opts.quiet else "\n".join(rep["verdicts"]))
    if opts.json_out:
        js = {
            "audio": rep.get("audio_node"),
            "charts": [{k: v for k, v in c.items() if k not in ("root",)}
                       for c in rep["charts"]],
        }
        with open(opts.json_out, "w", encoding="utf-8") as f:
            json.dump(js, f, ensure_ascii=False, indent=2, default=str)
        print("\nJSON 报告：%s" % opts.json_out)
    return 0


def analyze_audio_node(audio_path, opts, rep):
    try:
        pcm, sr = decode_audio(audio_path)
    except Exception as ex:
        rep["audio_error"] = str(ex)
        print("⚠ 音频分析失败：%s" % ex)
        return rep
    env, raw, fr = spectral_flux(pcm, sr)
    peaks = onset_peaks(env, raw, fr, frame_center_frames=FRAME_CENTER_FRAMES)
    bpm_raw, lag = estimate_tempo(env, fr)
    bpm = None
    if opts.bpm and opts.bpm > 0:
        bpm = opts.bpm
    elif bpm_raw > 0:
        bpm = pick_tempo_candidate(env, fr, bpm_raw, onsets=peaks)
    T = (60000.0 / bpm) if bpm else None
    phi = None
    if T:
        phi, ph_conf = refine_phase(peaks, T)
        if phi is None:
            phi = 0.0
        if opts.offset_ms is not None:
            phi = opts.offset_ms
        elif not (opts.bpm and opts.bpm > 0):
            # 拍网格最小二乘细化（分窗吸附 + 拼接回归）→ 高精度 BPM/相位
            phi, T, snap_ratio = refine_grid_local(peaks, phi, T)
            bpm = 60000.0 / T
    hits = 0
    beats_n = 0
    if T and phi is not None:
        ks = np.arange(0, int((len(env) * 1000.0 / fr) / T) + 1)
        beats = phi + ks * T
        i = np.searchsorted(peaks, beats)
        dl = np.full(len(beats), 1e18); dr = np.full(len(beats), 1e18)
        m = i < len(peaks); dl[m] = np.abs(peaks[i[m]] - beats[m])
        m = i > 0; dr[m] = np.abs(peaks[i[m] - 1] - beats[m])
        d = np.minimum(dl, dr)
        hits = int((d <= 60.0).sum())
        beats_n = len(beats)
    drift = tempo_drift(peaks, phi, T) if T else []
    rep["audio_node"] = {
        "path": audio_path, "sr": sr, "dur": len(pcm) / float(sr),
        "onsets": peaks.tolist(), "bpm": bpm, "T": T, "phi": phi,
        "hit_ratio": (hits / max(1, beats_n)) if beats_n else 0.0,
        "drift": drift,
    }
    rep["audio"] = rep["audio_node"]
    return rep


def build_verdicts(rep, opts):
    vs = []
    a = rep.get("audio_node")
    for ch in rep["charts"]:
        parts = []
        name = os.path.basename(ch["path"])
        if a and ch.get("scan"):
            s = ch["scan"]
            g = ch.get("grid")
            bpm_err = _bpm_err(ch["declared_bpm"], a["bpm"])
            med = g["stats"]["median"] if g else None
            phase_ok = True
            if ch["declared_offset"] is not None and a.get("phi") is not None:
                phase_ok = abs(ch["declared_offset"] - a["phi"]) <= 25.0 or \
                           abs(ch["declared_offset"] - a["phi"]) % max(1.0, a["T"]) <= 25.0
            good = (bpm_err is None or bpm_err <= 1.5) and (med is None or med <= opts.tol) and phase_ok
            if good:
                parts.append("✅ 对音良好（Δ %.1fms · BPM 差 %.1f%% · 网格中位 %.1fms）"
                             % (s["delta"], bpm_err or 0.0, med or 0.0))
            elif bpm_err is not None and bpm_err > 4.0:
                parts.append("⚠ BPM 与音频偏差 %.1f%%（谱面 %s / 音频 %s）——先校 BPM 再对音"
                             % (bpm_err, _fmt(ch["declared_bpm"]), _fmt(a["bpm"])))
            elif s["confidence"] < 0.4 and (med is None or med <= opts.tol):
                parts.append("⚠ 起音命中低（%.0f%%）但网格偏差小——音与音符不完全一一对应，正常；凭手感复核"
                             % (s["confidence"] * 100))
            else:
                parts.append("⚠ 对音一般（Δ %.1fms · 网格中位 %.1fms）——建议人工试听确认"
                             % (s["delta"], med or 0.0))
            if g and g["stats"]["le25"] < 0.85:
                parts.append("仅 %.0f%% 音符在 ±%dms 内" % (g["stats"]["le25"] * 100, opts.tol))
            off40 = int(round((1 - g["stats"]["le40"]) * len(ch["notes"]))) if g else 0
            if off40:
                parts.append(">40ms 偏差约 %d 个" % off40)
            if ch.get("chart_grid") and ch["chart_grid"]["hist"].get("other", 0) > 0.05:
                parts.append("谱面自身细分 %.0f%% 为非常规位置" % (ch["chart_grid"]["hist"]["other"] * 100))
        vs.append("・%s：%s" % (name, "；".join(parts) if parts else "（无音频）"))
    return vs


def build_actions(rep, opts, audio_path):
    actions = []
    a = rep.get("audio_node")
    for ch in rep["charts"]:
        if not (opts.fix_offset or opts.snap):
            continue
        if not a or not ch.get("scan"):
            actions.append("（%s：无音频分析结果，跳过写回）" % os.path.basename(ch["path"]))
            continue
        delta = ch["scan"]["delta"]
        grid = (a["phi"], a["T"], opts.grid, delta) if a.get("T") else None
        kind = ch_kind_of_path(ch["path"])
        try:
            if kind == "mil":
                m = apply_mil_fix(parse_chart(ch["path"]), delta, opts.invert, opts.snap, grid)
            elif kind == "osu":
                m = apply_osu_fix(parse_chart(ch["path"]), delta, opts.invert, opts.snap, grid)
            elif kind == "aff":
                m = apply_aff_fix(parse_chart(ch["path"]), delta, opts.invert, opts.snap, grid)
            elif kind == "phigros":
                m = apply_phigros_fix(parse_chart(ch["path"]), delta, opts.invert, opts.snap, grid)
            else:
                m = None
            if m:
                actions.append(m)
        except Exception as ex:
            actions.append("✗ %s：写回失败 %s" % (os.path.basename(ch["path"]), ex))
    return actions


def ch_kind_of_path(p):
    ext = os.path.splitext(p)[1].lower()
    if ext == ".mil":
        return "mil"
    if ext == ".osu":
        return "osu"
    if ext == ".aff":
        return "aff"
    if ext == ".json":
        return "phigros"
    return ""


if __name__ == "__main__":
    sys.exit(main())
