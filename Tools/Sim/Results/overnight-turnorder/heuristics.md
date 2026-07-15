# Heuristic report — turn-order

**Question:** Should south choose to go FIRST (vs second)?  
**Effect measured:** P(south wins | **first**) − P(south wins | **second**), in percentage points (pp), over matched-seed pairs. Positive ⇒ prefer *first*.

**Overall:** +7.2 pp  (95% CI [+7.0, +7.3], n=544,499)
_Promotion gate: n ≥ 3,000 paired trials AND the 95% CI excludes 0._

## Promoted findings (22)

**Finding:** prefer **first** when `my_low_curve = 0.1`.  
- Effect: +10.9 pp win probability (choosing first over second)  
- Validation: 66,000 paired trials, 95% CI [+10.4, +11.3] pp  
- Raw: first won 39,484, second won 32,317  

**Finding:** prefer **first** when `opp_low_curve = 0.1`.  
- Effect: +10.7 pp win probability (choosing first over second)  
- Validation: 66,000 paired trials, 95% CI [+10.3, +11.1] pp  
- Raw: first won 33,691, second won 26,617  

**Finding:** prefer **first** when `opp_low_curve = 0.0`.  
- Effect: +9.3 pp win probability (choosing first over second)  
- Validation: 16,500 paired trials, 95% CI [+8.4, +10.2] pp  
- Raw: first won 9,914, second won 8,380  

**Finding:** prefer **first** when `my_low_curve = 0.0`.  
- Effect: +8.7 pp win probability (choosing first over second)  
- Validation: 16,500 paired trials, 95% CI [+7.9, +9.6] pp  
- Raw: first won 8,036, second won 6,594  

**Finding:** prefer **first** when `opp_low_curve = 0.4`.  
- Effect: +8.1 pp win probability (choosing first over second)  
- Validation: 66,000 paired trials, 95% CI [+7.7, +8.5] pp  
- Raw: first won 43,840, second won 38,494  

**Finding:** prefer **first** when `opp_low_curve = 0.2`.  
- Effect: +8.0 pp win probability (choosing first over second)  
- Validation: 115,500 paired trials, 95% CI [+7.6, +8.3] pp  
- Raw: first won 56,032, second won 46,841  

**Finding:** prefer **first** when `my_low_curve = 0.4`.  
- Effect: +8.0 pp win probability (choosing first over second)  
- Validation: 66,000 paired trials, 95% CI [+7.6, +8.3] pp  
- Raw: first won 27,332, second won 22,085  

**Finding:** prefer **first** when `my_low_curve = 0.2`.  
- Effect: +7.9 pp win probability (choosing first over second)  
- Validation: 115,499 paired trials, 95% CI [+7.5, +8.2] pp  
- Raw: first won 68,368, second won 59,274  

**Finding:** prefer **first** when `opp_leader_life = 5`.  
- Effect: +7.4 pp win probability (choosing first over second)  
- Validation: 396,000 paired trials, 95% CI [+7.2, +7.5] pp  
- Raw: first won 223,770, second won 194,533  

**Finding:** prefer **first** when `opp_low_curve = 0.6`.  
- Effect: +7.3 pp win probability (choosing first over second)  
- Validation: 33,000 paired trials, 95% CI [+6.7, +7.9] pp  
- Raw: first won 18,611, second won 16,189  

**Finding:** prefer **first** when `my_leader_life = 5`.  
- Effect: +7.3 pp win probability (choosing first over second)  
- Validation: 395,999 paired trials, 95% CI [+7.2, +7.5] pp  
- Raw: first won 201,030, second won 171,985  

**Finding:** prefer **first** when `i_am_faster = false`.  
- Effect: +7.3 pp win probability (choosing first over second)  
- Validation: 281,999 paired trials, 95% CI [+7.1, +7.5] pp  
- Raw: first won 160,062, second won 139,590  

**Finding:** prefer **first** when `i_am_faster = true`.  
- Effect: +7.1 pp win probability (choosing first over second)  
- Validation: 262,500 paired trials, 95% CI [+6.9, +7.3] pp  
- Raw: first won 131,481, second won 112,939  

**Finding:** prefer **first** when `my_leader_life = 4`.  
- Effect: +6.9 pp win probability (choosing first over second)  
- Validation: 115,500 paired trials, 95% CI [+6.6, +7.2] pp  
- Raw: first won 66,112, second won 58,113  

**Finding:** prefer **first** when `opp_leader_life = 4`.  
- Effect: +6.6 pp win probability (choosing first over second)  
- Validation: 115,500 paired trials, 95% CI [+6.3, +6.9] pp  
- Raw: first won 57,194, second won 49,524  

**Finding:** prefer **first** when `opp_leader_life = 6+`.  
- Effect: +6.4 pp win probability (choosing first over second)  
- Validation: 32,999 paired trials, 95% CI [+5.8, +6.9] pp  
- Raw: first won 10,579, second won 8,472  

**Finding:** prefer **first** when `my_low_curve = 0.6`.  
- Effect: +6.3 pp win probability (choosing first over second)  
- Validation: 33,000 paired trials, 95% CI [+5.7, +6.9] pp  
- Raw: first won 16,628, second won 14,545  

**Finding:** prefer **first** when `my_low_curve = 0.3`.  
- Effect: +6.1 pp win probability (choosing first over second)  
- Validation: 165,000 paired trials, 95% CI [+5.9, +6.4] pp  
- Raw: first won 99,081, second won 88,948  

**Finding:** prefer **first** when `my_leader_life = 6+`.  
- Effect: +6.0 pp win probability (choosing first over second)  
- Validation: 33,000 paired trials, 95% CI [+5.4, +6.5] pp  
- Raw: first won 24,401, second won 22,431  

**Finding:** prefer **first** when `opp_low_curve = 0.3`.  
- Effect: +5.9 pp win probability (choosing first over second)  
- Validation: 164,999 paired trials, 95% CI [+5.7, +6.2] pp  
- Raw: first won 75,824, second won 66,056  

**Finding:** prefer **first** when `my_low_curve = 0.5`.  
- Effect: +4.7 pp win probability (choosing first over second)  
- Validation: 82,500 paired trials, 95% CI [+4.3, +5.0] pp  
- Raw: first won 32,614, second won 28,766  

**Finding:** prefer **first** when `opp_low_curve = 0.5`.  
- Effect: +4.5 pp win probability (choosing first over second)  
- Validation: 82,500 paired trials, 95% CI [+4.1, +4.8] pp  
- Raw: first won 53,631, second won 49,952  

## All feature buckets (ungated — includes reversals)

| condition | effect pp | 95% CI pp | n |
|---|---:|:---:|---:|
| opp_leader_life = 5 | +7.4 | [+7.2, +7.5] | 396,000 |
| my_leader_life = 5 | +7.3 | [+7.2, +7.5] | 395,999 |
| i_am_faster = false | +7.3 | [+7.1, +7.5] | 281,999 |
| i_am_faster = true | +7.1 | [+6.9, +7.3] | 262,500 |
| my_low_curve = 0.3 | +6.1 | [+5.9, +6.4] | 165,000 |
| opp_low_curve = 0.3 | +5.9 | [+5.7, +6.2] | 164,999 |
| opp_low_curve = 0.2 | +8.0 | [+7.6, +8.3] | 115,500 |
| my_leader_life = 4 | +6.9 | [+6.6, +7.2] | 115,500 |
| opp_leader_life = 4 | +6.6 | [+6.3, +6.9] | 115,500 |
| my_low_curve = 0.2 | +7.9 | [+7.5, +8.2] | 115,499 |
| my_low_curve = 0.5 | +4.7 | [+4.3, +5.0] | 82,500 |
| opp_low_curve = 0.5 | +4.5 | [+4.1, +4.8] | 82,500 |
| my_low_curve = 0.1 | +10.9 | [+10.4, +11.3] | 66,000 |
| my_low_curve = 0.4 | +8.0 | [+7.6, +8.3] | 66,000 |
| opp_low_curve = 0.4 | +8.1 | [+7.7, +8.5] | 66,000 |
| opp_low_curve = 0.1 | +10.7 | [+10.3, +11.1] | 66,000 |
| my_low_curve = 0.6 | +6.3 | [+5.7, +6.9] | 33,000 |
| opp_low_curve = 0.6 | +7.3 | [+6.7, +7.9] | 33,000 |
| my_leader_life = 6+ | +6.0 | [+5.4, +6.5] | 33,000 |
| opp_leader_life = 6+ | +6.4 | [+5.8, +6.9] | 32,999 |
| my_low_curve = 0.0 | +8.7 | [+7.9, +9.6] | 16,500 |
| opp_low_curve = 0.0 | +9.3 | [+8.4, +10.2] | 16,500 |
| matchup: st09 vs lt01luffy | +7.0 | [+4.2, +9.8] | 500 |
| matchup: st09 vs lt01nami | +7.6 | [+2.1, +13.1] | 500 |
| matchup: st09 vs lt01zoro | +33.6 | [+28.3, +38.9] | 500 |
| matchup: st09 vs st01 | +0.6 | [-4.4, +5.6] | 500 |
| matchup: st09 vs st02 | +11.8 | [+6.2, +17.4] | 500 |
| matchup: st09 vs st03 | +1.2 | [-1.5, +3.9] | 500 |
| matchup: st09 vs st04 | +11.4 | [+6.4, +16.4] | 500 |
| matchup: st09 vs st05 | +7.8 | [+2.5, +13.1] | 500 |
| matchup: st09 vs st06 | +17.4 | [+12.4, +22.4] | 500 |
| matchup: st09 vs st07 | +7.8 | [+2.7, +12.9] | 500 |
| matchup: st09 vs st08 | +26.0 | [+21.1, +30.9] | 500 |
| matchup: st09 vs st09 | +30.0 | [+24.6, +35.4] | 500 |
| matchup: st28 vs st06 | +6.6 | [+1.4, +11.8] | 500 |
| matchup: st28 vs st07 | +5.4 | [+0.1, +10.7] | 500 |
| matchup: st28 vs st08 | +4.8 | [+0.6, +9.0] | 500 |
| matchup: st28 vs st09 | +5.4 | [+0.4, +10.4] | 500 |
| matchup: st28 vs st10 | +6.4 | [+1.2, +11.6] | 500 |
| matchup: st28 vs st11 | +9.4 | [+4.4, +14.4] | 500 |
| matchup: st28 vs st12 | +0.6 | [-4.6, +5.8] | 500 |
| matchup: st28 vs st13 | +12.8 | [+7.7, +17.9] | 500 |
| matchup: st28 vs st14 | +8.6 | [+3.3, +13.9] | 500 |
| matchup: st28 vs st15 | +8.6 | [+4.0, +13.2] | 500 |
| matchup: st28 vs st16 | -0.4 | [-1.0, +0.2] | 500 |
| matchup: st28 vs st17 | -1.0 | [-6.5, +4.5] | 500 |
| matchup: st13 vs st02 | +22.0 | [+16.7, +27.3] | 500 |
| matchup: st13 vs st03 | +5.2 | [+1.1, +9.3] | 500 |
| matchup: st13 vs st04 | +15.6 | [+10.5, +20.7] | 500 |
| matchup: st13 vs st05 | +14.0 | [+8.3, +19.7] | 500 |
| matchup: st13 vs st06 | +18.2 | [+13.3, +23.1] | 500 |
| matchup: st13 vs st07 | +9.8 | [+4.7, +14.9] | 500 |
| matchup: st13 vs st08 | +20.0 | [+14.4, +25.6] | 500 |
| matchup: st13 vs st09 | +28.8 | [+23.4, +34.2] | 500 |
| matchup: st13 vs st10 | +22.2 | [+16.8, +27.6] | 500 |
| matchup: st13 vs st11 | +19.8 | [+14.7, +24.9] | 500 |
| matchup: st13 vs st12 | +15.8 | [+10.5, +21.1] | 500 |
| matchup: st13 vs st13 | +27.4 | [+22.0, +32.8] | 500 |
| matchup: st10 vs st10 | +4.6 | [-1.0, +10.2] | 500 |
| matchup: st10 vs st11 | +12.6 | [+7.2, +18.0] | 500 |
| matchup: st10 vs st12 | +6.2 | [+1.1, +11.3] | 500 |
| matchup: st10 vs st13 | +25.8 | [+20.4, +31.2] | 500 |
| matchup: st10 vs st14 | +3.4 | [-2.0, +8.8] | 500 |
| matchup: st10 vs st15 | +10.8 | [+6.3, +15.3] | 500 |
| matchup: st10 vs st16 | +1.4 | [-+0.0, +2.8] | 500 |
| matchup: st10 vs st17 | +3.8 | [-1.5, +9.1] | 500 |
| matchup: st10 vs st18 | +20.6 | [+15.8, +25.4] | 500 |
| matchup: st10 vs st19 | +0.8 | [-4.8, +6.4] | 500 |
| matchup: st10 vs st20 | +2.8 | [-2.6, +8.2] | 500 |
| matchup: st10 vs st21 | +1.0 | [-3.9, +5.9] | 500 |
| matchup: lt01nami vs st10 | +4.2 | [-1.5, +9.9] | 500 |
| matchup: lt01nami vs st11 | -6.4 | [-11.9, -0.9] | 500 |
| matchup: lt01nami vs st12 | -1.8 | [-7.3, +3.7] | 500 |
| matchup: lt01nami vs st13 | +13.4 | [+7.5, +19.3] | 500 |
| matchup: lt01nami vs st14 | +8.6 | [+3.0, +14.2] | 500 |
| matchup: lt01nami vs st15 | -0.4 | [-5.2, +4.4] | 500 |
| matchup: lt01nami vs st16 | +0.2 | [-1.0, +1.4] | 500 |
| matchup: lt01nami vs st17 | +7.4 | [+2.3, +12.5] | 500 |
| matchup: lt01nami vs st18 | +17.0 | [+11.4, +22.6] | 500 |
| matchup: lt01nami vs st19 | -10.2 | [-15.9, -4.5] | 500 |
| matchup: lt01nami vs st20 | -2.6 | [-7.8, +2.6] | 500 |
| matchup: lt01nami vs st21 | -5.8 | [-10.7, -0.9] | 500 |
| matchup: st20 vs lt01luffy | -1.2 | [-5.4, +3.0] | 500 |
| matchup: st20 vs lt01nami | -8.6 | [-13.9, -3.3] | 500 |
| matchup: st20 vs lt01zoro | -6.2 | [-11.5, -0.9] | 500 |
| matchup: st20 vs st01 | -9.6 | [-14.8, -4.4] | 500 |
| matchup: st20 vs st02 | -1.0 | [-6.2, +4.2] | 500 |
| matchup: st20 vs st03 | -1.0 | [-3.4, +1.4] | 500 |
| matchup: st20 vs st04 | -2.2 | [-6.4, +2.0] | 500 |
| matchup: st20 vs st05 | -3.4 | [-8.1, +1.3] | 500 |
| matchup: st20 vs st06 | +1.4 | [-4.3, +7.1] | 500 |
| matchup: st20 vs st07 | +1.2 | [-4.2, +6.6] | 500 |
| matchup: st20 vs st08 | +3.8 | [-1.0, +8.6] | 500 |
| matchup: st20 vs st09 | +4.4 | [-0.4, +9.2] | 500 |
| matchup: st06 vs st06 | +16.0 | [+10.6, +21.4] | 500 |
| matchup: st06 vs st07 | +4.4 | [-1.0, +9.8] | 500 |
| matchup: st06 vs st08 | +13.0 | [+8.6, +17.4] | 500 |
| matchup: st06 vs st09 | +17.8 | [+13.0, +22.6] | 500 |
| matchup: st06 vs st10 | +14.4 | [+8.9, +19.9] | 500 |
| matchup: st06 vs st11 | +19.4 | [+14.3, +24.5] | 500 |
| matchup: st06 vs st12 | +4.8 | [+0.3, +9.3] | 500 |
| matchup: st06 vs st13 | +18.0 | [+13.3, +22.7] | 500 |
| matchup: st06 vs st14 | +15.0 | [+9.6, +20.4] | 500 |
| matchup: st06 vs st15 | +18.6 | [+13.7, +23.5] | 500 |
| matchup: st06 vs st16 | +0.6 | [-0.1, +1.3] | 500 |
| matchup: st06 vs st17 | +7.0 | [+2.3, +11.7] | 500 |
| matchup: st03 vs st14 | +1.4 | [-1.3, +4.1] | 500 |
| matchup: st03 vs st15 | +1.2 | [-0.2, +2.6] | 500 |
| matchup: st03 vs st16 | -1.6 | [-5.2, +2.0] | 500 |
| matchup: st03 vs st17 | +3.8 | [+0.6, +7.0] | 500 |
| matchup: st03 vs st18 | +1.0 | [-0.6, +2.6] | 500 |
| matchup: st03 vs st19 | -2.8 | [-5.4, -0.2] | 500 |
| matchup: st03 vs st20 | +1.0 | [-1.1, +3.1] | 500 |
| matchup: st03 vs st21 | -0.4 | [-2.3, +1.5] | 500 |
| matchup: st03 vs st22 | +4.2 | [+0.6, +7.8] | 500 |
| matchup: st03 vs st23 | +0.6 | [-0.6, +1.8] | 500 |
| matchup: st03 vs st24 | +2.4 | [-0.3, +5.1] | 500 |
| matchup: st03 vs st25 | +5.6 | [+2.7, +8.5] | 500 |
| matchup: st02 vs st02 | +9.2 | [+3.7, +14.7] | 500 |
| matchup: st02 vs st03 | +2.8 | [+0.2, +5.4] | 500 |
| matchup: st02 vs st04 | +2.8 | [-1.8, +7.4] | 500 |
| matchup: st02 vs st05 | -3.6 | [-9.2, +2.0] | 500 |
| matchup: st02 vs st06 | +8.4 | [+3.0, +13.8] | 500 |
| matchup: st02 vs st07 | +6.0 | [+0.4, +11.6] | 500 |
| matchup: st02 vs st08 | +18.6 | [+13.4, +23.8] | 500 |
| matchup: st02 vs st09 | +16.6 | [+11.3, +21.9] | 500 |
| matchup: st02 vs st10 | +12.4 | [+6.9, +17.9] | 500 |
| matchup: st02 vs st11 | +12.6 | [+7.4, +17.8] | 500 |
| matchup: st02 vs st12 | +0.4 | [-5.1, +5.9] | 500 |
| matchup: st02 vs st13 | +19.6 | [+14.3, +24.9] | 500 |
| matchup: st07 vs st18 | +21.6 | [+16.2, +27.0] | 500 |
| matchup: st07 vs st19 | -4.0 | [-9.5, +1.5] | 500 |
| matchup: st07 vs st20 | +1.0 | [-4.7, +6.7] | 500 |
| matchup: st07 vs st21 | -1.8 | [-6.8, +3.2] | 500 |
| matchup: st07 vs st22 | +12.0 | [+6.9, +17.1] | 500 |
| matchup: st07 vs st23 | -4.8 | [-9.7, +0.1] | 500 |
| matchup: st07 vs st24 | +12.0 | [+6.9, +17.1] | 500 |
| matchup: st07 vs st25 | +21.2 | [+16.4, +26.0] | 500 |
| matchup: st07 vs st26 | +4.4 | [-0.4, +9.2] | 500 |
| matchup: st07 vs st27 | +12.0 | [+6.6, +17.4] | 500 |
| matchup: st07 vs st28 | +1.8 | [-3.7, +7.3] | 500 |
| matchup: st07 vs st29 | +9.2 | [+3.8, +14.6] | 500 |
| matchup: st21 vs st10 | +5.8 | [+1.2, +10.4] | 500 |
| matchup: st21 vs st11 | +3.2 | [-0.9, +7.3] | 500 |
| matchup: st21 vs st12 | +0.8 | [-3.0, +4.6] | 500 |
| matchup: st21 vs st13 | +11.6 | [+7.4, +15.8] | 500 |
| matchup: st21 vs st14 | -3.8 | [-8.8, +1.2] | 500 |
| matchup: st21 vs st15 | -5.8 | [-10.6, -1.0] | 500 |
| matchup: st21 vs st16 | -0.2 | [-0.6, +0.2] | 500 |
| matchup: st21 vs st17 | -3.0 | [-7.4, +1.4] | 500 |
| matchup: st21 vs st18 | +1.2 | [-3.6, +6.0] | 500 |
| matchup: st21 vs st19 | -7.4 | [-12.4, -2.4] | 500 |
| matchup: st21 vs st20 | -2.6 | [-7.9, +2.7] | 500 |
| matchup: st21 vs st21 | -6.4 | [-11.8, -1.0] | 500 |
| matchup: st15 vs st26 | +9.2 | [+4.9, +13.5] | 500 |
| matchup: st15 vs st27 | +18.2 | [+13.8, +22.6] | 500 |
| matchup: st15 vs st28 | +13.6 | [+8.8, +18.4] | 500 |
| matchup: st15 vs st29 | +5.6 | [+0.3, +10.9] | 500 |
| matchup: st15 vs st30 | +0.0 | [+0.0, +0.0] | 500 |
| matchup: st16 vs lt01luffy | +0.2 | [-0.2, +0.6] | 500 |
| matchup: st16 vs lt01nami | +1.0 | [-0.4, +2.4] | 500 |
| matchup: st16 vs lt01zoro | +0.0 | [-0.8, +0.8] | 500 |
| matchup: st16 vs st01 | -0.2 | [-0.6, +0.2] | 500 |
| matchup: st16 vs st02 | +0.0 | [-0.6, +0.6] | 500 |
| matchup: st16 vs st03 | -3.8 | [-7.1, -0.5] | 500 |
| matchup: st16 vs st04 | -2.2 | [-3.7, -0.7] | 500 |
| matchup: st16 vs st05 | -1.4 | [-2.4, -0.4] | 500 |
| matchup: st17 vs st06 | +4.4 | [-0.2, +9.0] | 500 |
| matchup: st17 vs st07 | -0.8 | [-5.7, +4.1] | 500 |
| matchup: st17 vs st08 | +5.0 | [+0.0, +10.0] | 500 |
| matchup: st17 vs st09 | +0.8 | [-4.6, +6.2] | 500 |
| matchup: st17 vs st10 | -0.4 | [-5.5, +4.7] | 500 |
| matchup: st17 vs st11 | +2.2 | [-2.8, +7.2] | 500 |
| matchup: st17 vs st12 | +7.2 | [+1.5, +12.9] | 500 |
| matchup: st17 vs st13 | +18.6 | [+13.1, +24.1] | 500 |
| matchup: st17 vs st14 | +0.8 | [-4.1, +5.7] | 500 |
| matchup: st17 vs st15 | +5.6 | [+2.6, +8.6] | 500 |
| matchup: st17 vs st16 | +0.2 | [-1.1, +1.5] | 500 |
| matchup: st17 vs st17 | +0.0 | [-5.4, +5.4] | 500 |
| matchup: st14 vs st14 | +18.2 | [+12.4, +24.0] | 500 |
| matchup: st14 vs st16 | +0.2 | [-0.5, +0.9] | 500 |
| matchup: st14 vs st17 | -0.6 | [-5.5, +4.3] | 500 |
| matchup: st14 vs st18 | +22.0 | [+16.6, +27.4] | 500 |
| matchup: st14 vs st19 | -1.8 | [-7.5, +3.9] | 500 |
| matchup: st14 vs st20 | -3.0 | [-8.4, +2.4] | 500 |
| matchup: st14 vs st21 | -3.8 | [-9.0, +1.4] | 500 |
| matchup: st14 vs st22 | +12.8 | [+7.7, +17.9] | 500 |
| matchup: st14 vs st23 | +6.6 | [+1.4, +11.8] | 500 |
| matchup: st14 vs st24 | +13.0 | [+7.8, +18.2] | 500 |
| matchup: st14 vs st25 | +38.0 | [+32.7, +43.3] | 500 |
| matchup: lt01zoro vs st22 | +15.0 | [+9.7, +20.3] | 500 |
| matchup: lt01zoro vs st23 | +1.2 | [-3.9, +6.3] | 500 |
| matchup: lt01zoro vs st24 | +14.4 | [+9.2, +19.6] | 500 |
| matchup: lt01zoro vs st25 | +27.4 | [+21.9, +32.9] | 500 |
| matchup: lt01zoro vs st26 | +20.2 | [+15.1, +25.3] | 500 |
| matchup: lt01zoro vs st27 | +21.8 | [+16.2, +27.4] | 500 |
| matchup: lt01zoro vs st28 | +8.0 | [+3.2, +12.8] | 500 |
| matchup: lt01zoro vs st29 | +7.6 | [+3.1, +12.1] | 500 |
| matchup: lt01zoro vs st30 | +0.0 | [+0.0, +0.0] | 500 |
| matchup: st01 vs lt01luffy | +2.2 | [-1.8, +6.2] | 500 |
| matchup: st01 vs lt01nami | -8.6 | [-14.1, -3.1] | 500 |
| matchup: st01 vs lt01zoro | +11.8 | [+6.5, +17.1] | 500 |
| matchup: st01 vs st01 | -4.2 | [-9.5, +1.1] | 500 |
| matchup: st11 vs st22 | +18.6 | [+13.7, +23.5] | 500 |
| matchup: st11 vs st23 | +2.2 | [-2.0, +6.4] | 500 |
| matchup: st11 vs st24 | +13.4 | [+8.0, +18.8] | 500 |
| matchup: st11 vs st25 | +30.6 | [+25.4, +35.8] | 500 |
| matchup: st11 vs st26 | +16.2 | [+11.1, +21.3] | 500 |
| matchup: st11 vs st27 | +22.4 | [+17.4, +27.4] | 500 |
| matchup: st11 vs st28 | +10.4 | [+5.4, +15.4] | 500 |
| matchup: st11 vs st29 | +9.0 | [+4.1, +13.9] | 500 |
| matchup: st11 vs st30 | +0.0 | [+0.0, +0.0] | 500 |
| matchup: st12 vs lt01luffy | +1.2 | [-1.5, +3.9] | 500 |
| matchup: st12 vs lt01nami | +0.2 | [-5.0, +5.4] | 500 |
| matchup: st12 vs lt01zoro | +6.8 | [+1.7, +11.9] | 500 |
| matchup: st12 vs st01 | +0.4 | [-5.1, +5.9] | 500 |
| matchup: st22 vs st22 | +16.8 | [+11.4, +22.2] | 500 |
| matchup: st22 vs st23 | +7.2 | [+3.0, +11.4] | 500 |
| matchup: st22 vs st24 | +22.6 | [+17.3, +27.9] | 500 |
| matchup: st22 vs st25 | +28.0 | [+22.7, +33.3] | 500 |
| matchup: st22 vs st26 | +16.0 | [+10.8, +21.2] | 500 |
| matchup: st22 vs st27 | +17.8 | [+12.4, +23.2] | 500 |
| matchup: st22 vs st28 | +5.2 | [+1.0, +9.4] | 500 |
| matchup: st22 vs st29 | +7.0 | [+2.7, +11.3] | 500 |
| matchup: st22 vs st30 | +0.0 | [+0.0, +0.0] | 500 |
| matchup: st23 vs lt01luffy | -11.6 | [-16.9, -6.3] | 500 |
| matchup: st23 vs lt01nami | -3.8 | [-8.7, +1.1] | 500 |
| matchup: st23 vs lt01zoro | +3.4 | [-1.5, +8.3] | 500 |
| matchup: st23 vs st01 | +1.2 | [-3.2, +5.6] | 500 |
| matchup: st04 vs st26 | +10.0 | [+5.0, +15.0] | 500 |
| matchup: st04 vs st27 | +9.8 | [+5.0, +14.6] | 500 |
| matchup: st04 vs st28 | +0.4 | [-4.0, +4.8] | 500 |
| matchup: st04 vs st29 | +0.4 | [-3.1, +3.9] | 500 |
| matchup: st04 vs st30 | +0.0 | [+0.0, +0.0] | 500 |
| matchup: st05 vs lt01luffy | +4.0 | [+0.8, +7.2] | 500 |
| matchup: st05 vs lt01nami | -5.2 | [-10.6, +0.2] | 500 |
| matchup: st05 vs lt01zoro | +1.0 | [-4.0, +6.0] | 500 |
| matchup: st05 vs st01 | -1.4 | [-6.5, +3.7] | 500 |
| matchup: st05 vs st02 | +3.4 | [-2.2, +9.0] | 500 |
| matchup: st05 vs st03 | -1.8 | [-4.8, +1.2] | 500 |
| matchup: st05 vs st04 | -1.6 | [-6.5, +3.3] | 500 |
| matchup: st05 vs st05 | -13.6 | [-19.2, -8.0] | 500 |
| matchup: st26 vs st26 | +15.2 | [+9.6, +20.8] | 500 |
| matchup: st26 vs st27 | +22.0 | [+16.8, +27.2] | 500 |
| matchup: st26 vs st28 | +2.6 | [-2.1, +7.3] | 500 |
| matchup: st26 vs st29 | +1.2 | [-3.8, +6.2] | 500 |
| matchup: st26 vs st30 | +0.0 | [+0.0, +0.0] | 500 |
| matchup: st27 vs lt01luffy | +4.4 | [+0.8, +8.0] | 500 |
| matchup: st27 vs lt01nami | +7.4 | [+1.8, +13.0] | 500 |
| matchup: st27 vs lt01zoro | +18.0 | [+12.6, +23.4] | 500 |
| matchup: st27 vs st01 | +1.6 | [-3.5, +6.7] | 500 |
| matchup: st27 vs st02 | +12.0 | [+6.7, +17.3] | 500 |
| matchup: st27 vs st03 | +2.4 | [-0.7, +5.5] | 500 |
| matchup: st27 vs st04 | +8.0 | [+3.4, +12.6] | 500 |
| matchup: st27 vs st05 | +9.2 | [+3.6, +14.8] | 500 |
| matchup: st29 vs st18 | +16.0 | [+10.4, +21.6] | 500 |
| matchup: st29 vs st19 | -5.0 | [-9.5, -0.5] | 500 |
| matchup: st29 vs st20 | -11.2 | [-16.3, -6.1] | 500 |
| matchup: st29 vs st21 | +2.2 | [-2.7, +7.1] | 500 |
| matchup: st29 vs st22 | +7.6 | [+3.2, +12.0] | 500 |
| matchup: st29 vs st23 | -11.2 | [-16.8, -5.6] | 500 |
| matchup: st29 vs st24 | +2.6 | [-1.8, +7.0] | 500 |
| matchup: st29 vs st25 | +21.2 | [+15.8, +26.6] | 500 |
| matchup: st29 vs st26 | -2.2 | [-7.1, +2.7] | 500 |
| matchup: st29 vs st27 | +6.2 | [+1.1, +11.3] | 500 |
| matchup: st29 vs st28 | +1.4 | [-3.6, +6.4] | 500 |
| matchup: st29 vs st29 | +0.2 | [-5.3, +5.7] | 500 |
| matchup: st24 vs st02 | +6.2 | [+0.6, +11.8] | 500 |
| matchup: st24 vs st03 | +1.2 | [-1.9, +4.3] | 500 |
| matchup: st24 vs st04 | +6.0 | [+1.1, +10.9] | 500 |
| matchup: st24 vs st05 | +8.2 | [+2.7, +13.7] | 500 |
| matchup: st24 vs st06 | +12.6 | [+7.7, +17.5] | 500 |
| matchup: st24 vs st07 | +9.8 | [+4.8, +14.8] | 500 |
| matchup: st24 vs st08 | +21.4 | [+16.3, +26.5] | 500 |
| matchup: st24 vs st09 | +23.0 | [+17.8, +28.2] | 500 |
| matchup: st24 vs st10 | +18.8 | [+13.4, +24.2] | 500 |
| matchup: st24 vs st11 | +19.0 | [+13.5, +24.5] | 500 |
| matchup: st24 vs st12 | +7.4 | [+1.7, +13.1] | 500 |
| matchup: st24 vs st13 | +32.4 | [+27.0, +37.8] | 500 |
| matchup: st18 vs st18 | +37.8 | [+32.3, +43.3] | 500 |
| matchup: st18 vs st19 | +8.0 | [+3.2, +12.8] | 500 |
| matchup: st18 vs st20 | +12.0 | [+7.0, +17.0] | 500 |
| matchup: st18 vs st21 | +4.8 | [-0.4, +10.0] | 500 |
| matchup: st18 vs st22 | +21.4 | [+17.0, +25.8] | 500 |
| matchup: st18 vs st23 | +8.8 | [+3.4, +14.2] | 500 |
| matchup: st18 vs st24 | +15.8 | [+11.8, +19.8] | 500 |
| matchup: st18 vs st25 | +28.2 | [+22.7, +33.7] | 500 |
| matchup: st18 vs st26 | +26.0 | [+20.8, +31.2] | 500 |
| matchup: st18 vs st27 | +25.0 | [+20.3, +29.7] | 500 |
| matchup: st18 vs st28 | +25.2 | [+19.8, +30.6] | 500 |
| matchup: st18 vs st29 | +8.2 | [+2.7, +13.7] | 500 |
| matchup: st25 vs st14 | +33.8 | [+28.2, +39.4] | 500 |
| matchup: st25 vs st15 | +15.4 | [+10.3, +20.5] | 500 |
| matchup: st25 vs st16 | +9.0 | [+6.3, +11.7] | 500 |
| matchup: st25 vs st17 | +11.6 | [+7.9, +15.3] | 500 |
| matchup: st25 vs st18 | +29.8 | [+24.4, +35.2] | 500 |
| matchup: st25 vs st19 | +20.8 | [+15.6, +26.0] | 500 |
| matchup: st25 vs st20 | +22.6 | [+17.5, +27.7] | 500 |
| matchup: st25 vs st21 | +16.0 | [+10.9, +21.1] | 500 |
| matchup: st25 vs st22 | +31.4 | [+26.3, +36.5] | 500 |
| matchup: st25 vs st23 | +9.8 | [+5.4, +14.2] | 500 |
| matchup: st25 vs st24 | +24.0 | [+18.9, +29.1] | 500 |
| matchup: st25 vs st25 | +39.6 | [+34.5, +44.7] | 500 |
| matchup: st16 vs st06 | -0.2 | [-0.9, +0.5] | 500 |
| matchup: st16 vs st07 | -0.4 | [-1.5, +0.7] | 500 |
| matchup: st16 vs st08 | +2.4 | [+0.3, +4.5] | 500 |
| matchup: st16 vs st09 | +0.6 | [-0.3, +1.5] | 500 |
| matchup: st16 vs st10 | -1.0 | [-2.3, +0.3] | 500 |
| matchup: st16 vs st11 | -0.4 | [-1.5, +0.7] | 500 |
| matchup: st16 vs st12 | -0.4 | [-1.2, +0.4] | 500 |
| matchup: st16 vs st13 | +2.0 | [+0.1, +3.9] | 500 |
| matchup: st16 vs st14 | +0.0 | [-0.8, +0.8] | 500 |
| matchup: st16 vs st15 | +0.4 | [-0.4, +1.2] | 500 |
| matchup: st16 vs st16 | -2.0 | [-6.0, +2.0] | 500 |
| matchup: st16 vs st17 | -0.6 | [-1.9, +0.7] | 500 |
| matchup: st16 vs st18 | +0.2 | [-0.7, +1.1] | 500 |
| matchup: st16 vs st19 | +0.0 | [-0.8, +0.8] | 500 |
| matchup: st16 vs st20 | +0.0 | [-0.8, +0.8] | 500 |
| matchup: st16 vs st21 | -0.2 | [-0.6, +0.2] | 500 |
| matchup: st16 vs st22 | +1.4 | [-0.5, +3.3] | 500 |
| matchup: st16 vs st23 | +0.0 | [+0.0, +0.0] | 500 |
| matchup: st16 vs st24 | -0.2 | [-0.9, +0.5] | 500 |
| matchup: st16 vs st25 | +8.4 | [+5.3, +11.5] | 500 |
| matchup: st16 vs st26 | +0.6 | [-0.4, +1.6] | 500 |
| matchup: st16 vs st27 | +0.2 | [-1.2, +1.6] | 500 |
| matchup: st09 vs st10 | +11.2 | [+5.5, +16.9] | 500 |
| matchup: st09 vs st11 | +24.8 | [+19.5, +30.1] | 500 |
| matchup: st09 vs st12 | +12.2 | [+6.6, +17.8] | 500 |
| matchup: st09 vs st13 | +25.0 | [+19.6, +30.4] | 500 |
| matchup: st09 vs st14 | +14.6 | [+9.2, +20.0] | 500 |
| matchup: st09 vs st15 | +11.0 | [+6.2, +15.8] | 500 |
| matchup: st09 vs st16 | +0.2 | [-0.7, +1.1] | 500 |
| matchup: st09 vs st17 | +8.8 | [+3.9, +13.7] | 500 |
| matchup: st09 vs st18 | +25.8 | [+21.3, +30.3] | 500 |
| matchup: st09 vs st19 | +11.0 | [+5.6, +16.4] | 500 |
| matchup: st09 vs st20 | +1.4 | [-3.1, +5.9] | 500 |
| matchup: st09 vs st21 | +1.0 | [-3.7, +5.7] | 500 |
| matchup: st09 vs st22 | +24.0 | [+18.8, +29.2] | 500 |
| matchup: st09 vs st23 | +0.0 | [-4.6, +4.6] | 500 |
| matchup: st09 vs st24 | +14.8 | [+9.4, +20.2] | 500 |
| matchup: st09 vs st25 | +31.0 | [+25.8, +36.2] | 500 |
| matchup: st09 vs st26 | +15.2 | [+9.8, +20.6] | 500 |
| matchup: st09 vs st27 | +24.8 | [+19.4, +30.2] | 500 |
| matchup: st09 vs st28 | +9.4 | [+4.5, +14.3] | 500 |
| matchup: st09 vs st29 | +4.4 | [-0.1, +8.9] | 500 |
| matchup: st09 vs st30 | +0.0 | [-0.6, +0.6] | 500 |
| matchup: st10 vs lt01luffy | +3.8 | [+0.2, +7.4] | 500 |
| matchup: st10 vs lt01nami | +1.4 | [-4.4, +7.2] | 500 |
| matchup: st13 vs st14 | +19.2 | [+13.5, +24.9] | 500 |
| matchup: st13 vs st15 | +7.4 | [+4.3, +10.5] | 500 |
| matchup: st13 vs st16 | +0.8 | [-1.0, +2.6] | 500 |
| matchup: st13 vs st17 | +15.0 | [+9.3, +20.7] | 500 |
| matchup: st13 vs st18 | +28.8 | [+23.8, +33.8] | 500 |
| matchup: st13 vs st19 | +15.2 | [+9.8, +20.6] | 500 |
| matchup: st13 vs st20 | +5.2 | [+0.4, +10.0] | 500 |
| matchup: st13 vs st21 | +13.8 | [+9.5, +18.1] | 500 |
| matchup: st13 vs st22 | +22.0 | [+16.4, +27.6] | 500 |
| matchup: st13 vs st23 | +12.6 | [+8.4, +16.8] | 500 |
| matchup: st13 vs st24 | +33.2 | [+27.8, +38.6] | 500 |
| matchup: st13 vs st25 | +32.0 | [+26.6, +37.4] | 500 |
| matchup: st13 vs st26 | +23.4 | [+18.2, +28.6] | 500 |
| matchup: st13 vs st27 | +25.6 | [+20.1, +31.1] | 500 |
| matchup: st13 vs st28 | +13.8 | [+8.7, +18.9] | 500 |
| matchup: st13 vs st29 | +22.6 | [+17.7, +27.5] | 500 |
| matchup: st13 vs st30 | +0.0 | [+0.0, +0.0] | 500 |
| matchup: st14 vs lt01luffy | +9.2 | [+5.1, +13.3] | 500 |
| matchup: st14 vs lt01nami | +9.6 | [+3.7, +15.5] | 500 |
| matchup: st14 vs lt01zoro | +24.4 | [+18.7, +30.1] | 500 |
| matchup: st14 vs st01 | +4.8 | [-0.6, +10.2] | 500 |
| matchup: st14 vs st02 | +3.2 | [-2.4, +8.8] | 500 |
| matchup: st14 vs st03 | +4.0 | [+1.4, +6.6] | 500 |
| matchup: lt01nami vs st22 | +5.4 | [-0.1, +10.9] | 500 |
| matchup: lt01nami vs st23 | -2.4 | [-7.5, +2.7] | 500 |
| matchup: lt01nami vs st24 | +9.8 | [+4.2, +15.4] | 500 |
| matchup: lt01nami vs st25 | +34.0 | [+28.8, +39.2] | 500 |
| matchup: lt01nami vs st26 | +4.2 | [-1.3, +9.7] | 500 |
| matchup: lt01nami vs st27 | +11.8 | [+6.4, +17.2] | 500 |
| matchup: lt01nami vs st28 | +3.8 | [-1.7, +9.3] | 500 |
| matchup: lt01nami vs st29 | -10.0 | [-15.7, -4.3] | 500 |
| matchup: lt01nami vs st30 | +0.0 | [+0.0, +0.0] | 500 |
| matchup: lt01zoro vs lt01luffy | +7.0 | [+3.3, +10.7] | 500 |
| matchup: lt01zoro vs lt01nami | +19.8 | [+14.4, +25.2] | 500 |
| matchup: lt01zoro vs lt01zoro | +17.6 | [+11.7, +23.5] | 500 |
| matchup: lt01zoro vs st01 | +8.6 | [+3.2, +14.0] | 500 |
| matchup: lt01zoro vs st02 | +13.0 | [+7.4, +18.6] | 500 |
| matchup: lt01zoro vs st03 | +3.2 | [+0.6, +5.8] | 500 |
| matchup: lt01zoro vs st04 | +6.4 | [+1.8, +11.0] | 500 |
| matchup: lt01zoro vs st05 | -0.8 | [-6.2, +4.6] | 500 |
| matchup: lt01zoro vs st06 | +11.6 | [+6.7, +16.5] | 500 |
| matchup: lt01zoro vs st07 | +13.2 | [+7.7, +18.7] | 500 |
| matchup: lt01zoro vs st08 | +21.0 | [+15.9, +26.1] | 500 |
| matchup: lt01zoro vs st09 | +33.8 | [+28.4, +39.2] | 500 |
| matchup: lt01zoro vs st10 | +4.0 | [-1.7, +9.7] | 500 |
| matchup: lt01zoro vs st11 | +18.0 | [+13.0, +23.0] | 500 |
| matchup: st10 vs st22 | +12.8 | [+7.3, +18.3] | 500 |
| matchup: st10 vs st23 | +0.6 | [-3.9, +5.1] | 500 |
| matchup: st10 vs st24 | +12.6 | [+7.1, +18.1] | 500 |
| matchup: st10 vs st25 | +31.6 | [+26.5, +36.7] | 500 |
| matchup: st10 vs st26 | +3.6 | [-1.7, +8.9] | 500 |
| matchup: st10 vs st27 | +9.2 | [+3.7, +14.7] | 500 |
| matchup: st10 vs st28 | +5.8 | [+0.7, +10.9] | 500 |
| matchup: st10 vs st29 | +2.2 | [-2.6, +7.0] | 500 |
| matchup: st10 vs st30 | +0.0 | [+0.0, +0.0] | 500 |
| matchup: st11 vs lt01luffy | +8.6 | [+5.0, +12.2] | 500 |
| matchup: st11 vs lt01nami | -0.4 | [-6.1, +5.3] | 500 |
| matchup: st11 vs lt01zoro | +23.4 | [+18.1, +28.7] | 500 |
| matchup: st11 vs st01 | -0.2 | [-5.0, +4.6] | 500 |
| matchup: st11 vs st02 | +11.6 | [+6.3, +16.9] | 500 |
| matchup: st11 vs st03 | +2.0 | [-0.5, +4.5] | 500 |
| matchup: st11 vs st04 | +8.4 | [+3.7, +13.1] | 500 |
| matchup: st11 vs st05 | +4.0 | [-1.0, +9.0] | 500 |
| matchup: st11 vs st06 | +19.0 | [+13.9, +24.1] | 500 |
| matchup: st11 vs st07 | +7.2 | [+2.2, +12.2] | 500 |
| matchup: st11 vs st08 | +16.8 | [+12.0, +21.6] | 500 |
| matchup: st11 vs st09 | +14.2 | [+8.9, +19.5] | 500 |
| matchup: st11 vs st10 | +12.4 | [+6.8, +18.0] | 500 |
| matchup: st11 vs st11 | +17.0 | [+11.9, +22.1] | 500 |
| matchup: st07 vs st30 | +0.0 | [+0.0, +0.0] | 500 |
| matchup: st08 vs lt01luffy | +9.8 | [+6.6, +13.0] | 500 |
| matchup: st08 vs lt01nami | +7.0 | [+2.0, +12.0] | 500 |
| matchup: st08 vs lt01zoro | +24.4 | [+19.3, +29.5] | 500 |
| matchup: st08 vs st01 | +4.0 | [-1.1, +9.1] | 500 |
| matchup: st08 vs st02 | +17.0 | [+11.7, +22.3] | 500 |
| matchup: st08 vs st03 | +4.6 | [+0.9, +8.3] | 500 |
| matchup: st08 vs st04 | +10.4 | [+5.6, +15.2] | 500 |
| matchup: st08 vs st05 | +5.0 | [-0.2, +10.2] | 500 |
| matchup: st08 vs st06 | +20.2 | [+15.6, +24.8] | 500 |
| matchup: st08 vs st07 | +6.8 | [+1.7, +11.9] | 500 |
| matchup: st08 vs st08 | +23.0 | [+18.2, +27.8] | 500 |
| matchup: st08 vs st09 | +24.2 | [+19.3, +29.1] | 500 |
| matchup: st08 vs st10 | +9.0 | [+3.6, +14.4] | 500 |
| matchup: st08 vs st11 | +17.2 | [+12.6, +21.8] | 500 |
| matchup: st08 vs st12 | +8.2 | [+3.0, +13.4] | 500 |
| matchup: st08 vs st13 | +19.0 | [+13.3, +24.7] | 500 |
| matchup: st08 vs st14 | +20.2 | [+15.2, +25.2] | 500 |
| matchup: st08 vs st15 | +13.8 | [+9.8, +17.8] | 500 |
| matchup: st08 vs st16 | +0.0 | [-1.9, +1.9] | 500 |
| matchup: st08 vs st17 | +8.4 | [+3.4, +13.4] | 500 |
| matchup: st08 vs st18 | +20.4 | [+16.5, +24.3] | 500 |
| matchup: st08 vs st19 | +10.4 | [+5.7, +15.1] | 500 |
| matchup: st03 vs st26 | +3.8 | [+0.4, +7.2] | 500 |
| matchup: st03 vs st27 | +1.0 | [-1.6, +3.6] | 500 |
| matchup: st03 vs st28 | -1.2 | [-4.1, +1.7] | 500 |
| matchup: st03 vs st29 | -0.4 | [-1.9, +1.1] | 500 |
| matchup: st03 vs st30 | +0.0 | [+0.0, +0.0] | 500 |
| matchup: st04 vs lt01luffy | +4.2 | [+1.3, +7.1] | 500 |
| matchup: st04 vs lt01nami | +1.0 | [-4.1, +6.1] | 500 |
| matchup: st04 vs lt01zoro | +7.0 | [+2.2, +11.8] | 500 |
| matchup: st04 vs st01 | +2.2 | [-2.5, +6.9] | 500 |
| matchup: st04 vs st02 | -0.2 | [-5.1, +4.7] | 500 |
| matchup: st04 vs st03 | +2.6 | [-1.1, +6.3] | 500 |
| matchup: st04 vs st04 | +0.6 | [-4.5, +5.7] | 500 |
| matchup: st04 vs st05 | -5.6 | [-10.8, -0.4] | 500 |
| matchup: st04 vs st06 | +10.8 | [+6.4, +15.2] | 500 |
| matchup: st04 vs st07 | +3.4 | [-1.3, +8.1] | 500 |
| matchup: st04 vs st08 | +12.8 | [+8.1, +17.5] | 500 |
| matchup: st04 vs st09 | +9.6 | [+5.1, +14.1] | 500 |
| matchup: st04 vs st10 | +1.4 | [-3.2, +6.0] | 500 |
| matchup: st04 vs st11 | +9.8 | [+4.9, +14.7] | 500 |
| matchup: st04 vs st12 | +2.0 | [-3.3, +7.3] | 500 |
| matchup: st04 vs st13 | +13.2 | [+7.8, +18.6] | 500 |
| matchup: st04 vs st14 | +9.0 | [+4.4, +13.6] | 500 |
| matchup: st04 vs st15 | +6.2 | [+2.9, +9.5] | 500 |
| matchup: st28 vs st18 | +21.4 | [+16.2, +26.6] | 500 |
| matchup: st28 vs st19 | +2.4 | [-3.3, +8.1] | 500 |
| matchup: st28 vs st20 | +8.8 | [+3.5, +14.1] | 500 |
| matchup: st28 vs st21 | +4.8 | [-0.4, +10.0] | 500 |
| matchup: st28 vs st22 | +10.8 | [+6.8, +14.8] | 500 |
| matchup: st28 vs st23 | +2.4 | [-3.3, +8.1] | 500 |
| matchup: st28 vs st24 | +15.0 | [+10.0, +20.0] | 500 |
| matchup: st28 vs st25 | +18.2 | [+12.8, +23.6] | 500 |
| matchup: st28 vs st26 | +3.8 | [-0.6, +8.2] | 500 |
| matchup: st28 vs st27 | +9.8 | [+4.7, +14.9] | 500 |
| matchup: st28 vs st28 | +10.6 | [+4.9, +16.3] | 500 |
| matchup: st28 vs st29 | +0.6 | [-4.6, +5.8] | 500 |
| matchup: st28 vs st30 | +0.0 | [+0.0, +0.0] | 500 |
| matchup: st29 vs lt01luffy | +0.8 | [-4.4, +6.0] | 500 |
| matchup: st29 vs lt01nami | -8.6 | [-14.2, -3.0] | 500 |
| matchup: st29 vs lt01zoro | +7.8 | [+3.0, +12.6] | 500 |
| matchup: st29 vs st01 | -6.0 | [-10.6, -1.4] | 500 |
| matchup: st29 vs st02 | -1.8 | [-6.5, +2.9] | 500 |
| matchup: st29 vs st03 | -0.6 | [-2.4, +1.2] | 500 |
| matchup: st29 vs st04 | +0.2 | [-3.1, +3.5] | 500 |
| matchup: st29 vs st05 | -2.8 | [-7.4, +1.8] | 500 |
| matchup: st29 vs st06 | +10.2 | [+5.1, +15.3] | 500 |
| matchup: st29 vs st07 | +9.8 | [+4.5, +15.1] | 500 |
| matchup: st20 vs st10 | +5.0 | [-0.3, +10.3] | 500 |
| matchup: st20 vs st11 | +4.4 | [-0.4, +9.2] | 500 |
| matchup: st20 vs st12 | -4.2 | [-9.1, +0.7] | 500 |
| matchup: st20 vs st13 | +6.4 | [+1.6, +11.2] | 500 |
| matchup: st20 vs st14 | +3.6 | [-1.8, +9.0] | 500 |
| matchup: st20 vs st15 | +1.8 | [-3.9, +7.5] | 500 |
| matchup: st20 vs st16 | +0.2 | [-0.7, +1.1] | 500 |
| matchup: st20 vs st17 | -9.2 | [-14.0, -4.4] | 500 |
| matchup: st20 vs st18 | +12.6 | [+7.4, +17.8] | 500 |
| matchup: st20 vs st19 | -16.6 | [-22.0, -11.2] | 500 |
| matchup: st20 vs st20 | -5.4 | [-10.9, +0.1] | 500 |
| matchup: st20 vs st21 | -3.0 | [-8.2, +2.2] | 500 |
| matchup: st20 vs st22 | +7.4 | [+2.4, +12.4] | 500 |
| matchup: st20 vs st23 | -7.0 | [-12.2, -1.8] | 500 |
| matchup: st20 vs st24 | +5.6 | [+0.4, +10.8] | 500 |
| matchup: st20 vs st25 | +23.8 | [+18.4, +29.2] | 500 |
| matchup: st20 vs st26 | +3.8 | [-1.1, +8.7] | 500 |
| matchup: st20 vs st27 | +6.8 | [+1.4, +12.2] | 500 |
| matchup: st20 vs st28 | +6.0 | [+0.5, +11.5] | 500 |
| matchup: st20 vs st29 | -4.8 | [-9.9, +0.3] | 500 |
| matchup: st20 vs st30 | -0.2 | [-0.6, +0.2] | 500 |
| matchup: st21 vs lt01luffy | -3.0 | [-7.8, +1.8] | 500 |
| matchup: st21 vs lt01nami | -9.8 | [-14.7, -4.9] | 500 |
| matchup: st01 vs st02 | +1.8 | [-3.4, +7.0] | 500 |
| matchup: st01 vs st03 | +0.0 | [-2.8, +2.8] | 500 |
| matchup: st01 vs st04 | -2.6 | [-7.3, +2.1] | 500 |
| matchup: st01 vs st05 | -10.8 | [-15.9, -5.7] | 500 |
| matchup: st01 vs st06 | -1.8 | [-7.2, +3.6] | 500 |
| matchup: st01 vs st07 | -4.2 | [-9.2, +0.8] | 500 |
| matchup: st01 vs st08 | +1.2 | [-4.0, +6.4] | 500 |
| matchup: st01 vs st09 | +2.6 | [-2.6, +7.8] | 500 |
| matchup: st01 vs st10 | +2.0 | [-3.3, +7.3] | 500 |
| matchup: st01 vs st11 | +1.6 | [-3.3, +6.5] | 500 |
| matchup: st01 vs st12 | -2.2 | [-7.4, +3.0] | 500 |
| matchup: st01 vs st13 | +7.2 | [+2.2, +12.2] | 500 |
| matchup: st01 vs st14 | +1.2 | [-4.1, +6.5] | 500 |
| matchup: st01 vs st15 | +0.2 | [-3.6, +4.0] | 500 |
| matchup: st01 vs st16 | -0.6 | [-1.3, +0.1] | 500 |
| matchup: st01 vs st17 | +1.0 | [-4.5, +6.5] | 500 |
| matchup: st01 vs st18 | +5.6 | [+1.1, +10.1] | 500 |
| matchup: st01 vs st19 | -3.0 | [-8.3, +2.3] | 500 |
| matchup: st01 vs st20 | -5.2 | [-10.4, -+0.0] | 500 |
| matchup: st01 vs st21 | +1.6 | [-2.9, +6.1] | 500 |
| matchup: st01 vs st22 | +5.2 | [-+0.0, +10.4] | 500 |
| matchup: st01 vs st23 | +2.0 | [-2.1, +6.1] | 500 |
| matchup: st06 vs st18 | +23.6 | [+18.4, +28.8] | 500 |
| matchup: st06 vs st19 | -0.6 | [-5.9, +4.7] | 500 |
| matchup: st06 vs st20 | +6.4 | [+1.3, +11.5] | 500 |
| matchup: st06 vs st21 | +1.8 | [-3.0, +6.6] | 500 |
| matchup: st06 vs st22 | +17.4 | [+12.4, +22.4] | 500 |
| matchup: st06 vs st23 | +0.2 | [-4.7, +5.1] | 500 |
| matchup: st06 vs st24 | +17.2 | [+12.0, +22.4] | 500 |
| matchup: st06 vs st25 | +25.0 | [+19.8, +30.2] | 500 |
| matchup: st06 vs st26 | +6.4 | [+1.4, +11.4] | 500 |
| matchup: st06 vs st27 | +19.2 | [+14.3, +24.1] | 500 |
| matchup: st06 vs st28 | +10.2 | [+4.6, +15.8] | 500 |
| matchup: st06 vs st29 | +5.6 | [+0.6, +10.6] | 500 |
| matchup: st06 vs st30 | +0.0 | [+0.0, +0.0] | 500 |
| matchup: st07 vs lt01luffy | -3.2 | [-7.7, +1.3] | 500 |
| matchup: st07 vs lt01nami | +0.8 | [-4.6, +6.2] | 500 |
| matchup: st07 vs lt01zoro | +17.4 | [+12.1, +22.7] | 500 |
| matchup: st07 vs st01 | -0.8 | [-5.9, +4.3] | 500 |
| matchup: st07 vs st02 | +2.2 | [-3.2, +7.6] | 500 |
| matchup: st07 vs st03 | +0.8 | [-1.9, +3.5] | 500 |
| matchup: st07 vs st04 | -2.8 | [-7.5, +1.9] | 500 |
| matchup: st07 vs st05 | -1.2 | [-6.3, +3.9] | 500 |
| matchup: st07 vs st06 | +7.2 | [+1.7, +12.7] | 500 |
| matchup: st07 vs st07 | -2.6 | [-8.1, +2.9] | 500 |
| matchup: st21 vs st22 | +6.0 | [+1.3, +10.7] | 500 |
| matchup: st21 vs st23 | -2.8 | [-8.2, +2.6] | 500 |
| matchup: st21 vs st24 | +7.4 | [+2.7, +12.1] | 500 |
| matchup: st21 vs st25 | +22.6 | [+17.8, +27.4] | 500 |
| matchup: st21 vs st26 | +1.0 | [-4.1, +6.1] | 500 |
| matchup: st21 vs st27 | +4.4 | [-0.4, +9.2] | 500 |
| matchup: st21 vs st28 | +2.6 | [-2.8, +8.0] | 500 |
| matchup: st21 vs st29 | +2.4 | [-2.7, +7.5] | 500 |
| matchup: st21 vs st30 | +0.0 | [+0.0, +0.0] | 500 |
| matchup: st22 vs lt01luffy | +9.0 | [+5.6, +12.4] | 500 |
| matchup: st22 vs lt01nami | +11.8 | [+6.4, +17.2] | 500 |
| matchup: st22 vs lt01zoro | +14.0 | [+8.7, +19.3] | 500 |
| matchup: st22 vs st01 | +7.6 | [+2.8, +12.4] | 500 |
| matchup: st22 vs st02 | +12.6 | [+7.2, +18.0] | 500 |
| matchup: st22 vs st03 | +4.0 | [+0.5, +7.5] | 500 |
| matchup: st22 vs st04 | +10.8 | [+6.1, +15.5] | 500 |
| matchup: st22 vs st05 | +9.2 | [+3.8, +14.6] | 500 |
| matchup: st22 vs st06 | +16.0 | [+11.1, +20.9] | 500 |
| matchup: st22 vs st07 | +8.6 | [+3.7, +13.5] | 500 |
| matchup: st22 vs st08 | +22.0 | [+16.8, +27.2] | 500 |
| matchup: st22 vs st09 | +21.6 | [+16.4, +26.8] | 500 |
| matchup: st22 vs st10 | +12.4 | [+7.1, +17.7] | 500 |
| matchup: st22 vs st11 | +17.6 | [+12.4, +22.8] | 500 |
| matchup: st12 vs st02 | +5.4 | [-+0.0, +10.8] | 500 |
| matchup: st12 vs st03 | +4.0 | [+0.1, +7.9] | 500 |
| matchup: st12 vs st04 | -0.4 | [-5.6, +4.8] | 500 |
| matchup: st12 vs st05 | -4.0 | [-9.5, +1.5] | 500 |
| matchup: st12 vs st06 | +12.4 | [+7.4, +17.4] | 500 |
| matchup: st12 vs st07 | +2.0 | [-3.1, +7.1] | 500 |
| matchup: st12 vs st08 | +3.2 | [-1.9, +8.3] | 500 |
| matchup: st12 vs st09 | +10.2 | [+4.5, +15.9] | 500 |
| matchup: st12 vs st10 | +7.6 | [+2.1, +13.1] | 500 |
| matchup: st12 vs st11 | +6.6 | [+1.4, +11.8] | 500 |
| matchup: st12 vs st12 | +6.4 | [+0.7, +12.1] | 500 |
| matchup: st12 vs st13 | +21.4 | [+16.0, +26.8] | 500 |
| matchup: st12 vs st14 | +3.4 | [-1.9, +8.7] | 500 |
| matchup: st12 vs st15 | +1.6 | [-1.1, +4.3] | 500 |
| matchup: st12 vs st16 | -0.6 | [-1.5, +0.3] | 500 |
| matchup: st12 vs st17 | +5.8 | [+0.2, +11.4] | 500 |
| matchup: st12 vs st18 | +4.4 | [+1.1, +7.7] | 500 |
| matchup: st12 vs st19 | -3.0 | [-8.0, +2.0] | 500 |
| matchup: st12 vs st20 | -1.2 | [-5.6, +3.2] | 500 |
| matchup: st12 vs st21 | +2.8 | [-1.6, +7.2] | 500 |
| matchup: st12 vs st22 | +10.4 | [+4.9, +15.9] | 500 |
| matchup: st12 vs st23 | -2.0 | [-5.0, +1.0] | 500 |
| matchup: st02 vs st14 | +9.4 | [+3.6, +15.2] | 500 |
| matchup: st02 vs st15 | +9.4 | [+4.7, +14.1] | 500 |
| matchup: st02 vs st16 | +0.0 | [-0.8, +0.8] | 500 |
| matchup: st02 vs st17 | +5.6 | [+0.5, +10.7] | 500 |
| matchup: st02 vs st18 | +19.2 | [+15.1, +23.3] | 500 |
| matchup: st02 vs st19 | -2.0 | [-7.4, +3.4] | 500 |
| matchup: st02 vs st20 | +0.6 | [-4.5, +5.7] | 500 |
| matchup: st02 vs st21 | -2.4 | [-7.3, +2.5] | 500 |
| matchup: st02 vs st22 | +12.0 | [+6.7, +17.3] | 500 |
| matchup: st02 vs st23 | -3.6 | [-8.0, +0.8] | 500 |
| matchup: st02 vs st24 | +17.2 | [+11.7, +22.7] | 500 |
| matchup: st02 vs st25 | +23.8 | [+18.6, +29.0] | 500 |
| matchup: st02 vs st26 | +7.6 | [+1.9, +13.3] | 500 |
| matchup: st02 vs st27 | +4.8 | [-0.6, +10.2] | 500 |
| matchup: st02 vs st28 | +8.2 | [+2.5, +13.9] | 500 |
| matchup: st02 vs st29 | +0.6 | [-4.0, +5.2] | 500 |
| matchup: st02 vs st30 | +0.0 | [+0.0, +0.0] | 500 |
| matchup: st03 vs lt01luffy | +2.2 | [+0.8, +3.6] | 500 |
| matchup: st03 vs lt01nami | -2.4 | [-5.4, +0.6] | 500 |
| matchup: st03 vs lt01zoro | +2.2 | [-0.5, +4.9] | 500 |
| matchup: st03 vs st01 | +1.6 | [-1.5, +4.7] | 500 |
| matchup: st03 vs st02 | +4.4 | [+1.9, +6.9] | 500 |
| matchup: st03 vs st03 | -1.8 | [-6.3, +2.7] | 500 |
| matchup: st23 vs st02 | -1.4 | [-5.5, +2.7] | 500 |
| matchup: st23 vs st03 | +1.0 | [-+0.0, +2.0] | 500 |
| matchup: st23 vs st04 | -1.0 | [-4.1, +2.1] | 500 |
| matchup: st23 vs st05 | -5.2 | [-9.8, -0.6] | 500 |
| matchup: st23 vs st06 | +0.2 | [-4.9, +5.3] | 500 |
| matchup: st23 vs st07 | -4.4 | [-9.3, +0.5] | 500 |
| matchup: st23 vs st08 | +5.2 | [+1.2, +9.2] | 500 |
| matchup: st23 vs st09 | +2.0 | [-2.5, +6.5] | 500 |
| matchup: st23 vs st10 | -2.8 | [-7.2, +1.6] | 500 |
| matchup: st23 vs st11 | +1.0 | [-3.5, +5.5] | 500 |
| matchup: st23 vs st12 | +1.6 | [-1.3, +4.5] | 500 |
| matchup: st23 vs st13 | +6.6 | [+2.7, +10.5] | 500 |
| matchup: st23 vs st14 | +1.6 | [-3.6, +6.8] | 500 |
| matchup: st23 vs st15 | +8.8 | [+3.4, +14.2] | 500 |
| matchup: st23 vs st16 | -0.2 | [-0.6, +0.2] | 500 |
| matchup: st23 vs st17 | +0.6 | [-2.6, +3.8] | 500 |
| matchup: st23 vs st18 | +2.6 | [-3.0, +8.2] | 500 |
| matchup: st23 vs st19 | -9.4 | [-14.3, -4.5] | 500 |
| matchup: st23 vs st20 | -13.8 | [-19.2, -8.4] | 500 |
| matchup: st23 vs st21 | +0.4 | [-5.1, +5.9] | 500 |
| matchup: st23 vs st22 | +8.6 | [+4.1, +13.1] | 500 |
| matchup: st23 vs st23 | -4.8 | [-10.6, +1.0] | 500 |
| matchup: st10 vs lt01zoro | +7.4 | [+1.9, +12.9] | 500 |
| matchup: st10 vs st01 | +0.4 | [-4.7, +5.5] | 500 |
| matchup: st10 vs st02 | +5.6 | [+0.2, +11.0] | 500 |
| matchup: st10 vs st03 | +2.0 | [-0.8, +4.8] | 500 |
| matchup: st10 vs st04 | +0.0 | [-4.8, +4.8] | 500 |
| matchup: st10 vs st05 | +3.8 | [-1.6, +9.2] | 500 |
| matchup: st10 vs st06 | +13.0 | [+7.6, +18.4] | 500 |
| matchup: st10 vs st07 | +6.6 | [+1.4, +11.8] | 500 |
| matchup: st10 vs st08 | +9.2 | [+3.9, +14.5] | 500 |
| matchup: st10 vs st09 | +10.6 | [+4.9, +16.3] | 500 |
| matchup: st18 vs st30 | +0.0 | [+0.0, +0.0] | 500 |
| matchup: st19 vs lt01luffy | +2.0 | [-1.6, +5.6] | 500 |
| matchup: st19 vs lt01nami | -6.4 | [-11.9, -0.9] | 500 |
| matchup: st19 vs lt01zoro | +3.2 | [-2.5, +8.9] | 500 |
| matchup: st19 vs st01 | -8.8 | [-14.2, -3.4] | 500 |
| matchup: st19 vs st02 | -0.4 | [-6.1, +5.3] | 500 |
| matchup: st19 vs st03 | -2.0 | [-4.7, +0.7] | 500 |
| matchup: st19 vs st04 | +1.2 | [-3.6, +6.0] | 500 |
| matchup: st19 vs st05 | -9.6 | [-14.9, -4.3] | 500 |
| matchup: st19 vs st06 | +5.6 | [+0.2, +11.0] | 500 |
| matchup: st19 vs st07 | +1.2 | [-4.1, +6.5] | 500 |
| matchup: st19 vs st08 | +6.8 | [+1.8, +11.8] | 500 |
| matchup: st19 vs st09 | +2.0 | [-3.5, +7.5] | 500 |
| matchup: st19 vs st10 | +2.6 | [-2.9, +8.1] | 500 |
| matchup: st19 vs st11 | +8.0 | [+2.9, +13.1] | 500 |
| matchup: st19 vs st12 | -0.8 | [-5.9, +4.3] | 500 |
| matchup: st19 vs st13 | +14.8 | [+9.3, +20.3] | 500 |
| matchup: st19 vs st14 | -2.2 | [-8.0, +3.6] | 500 |
| matchup: st19 vs st15 | +18.2 | [+12.9, +23.5] | 500 |
| matchup: st19 vs st16 | -0.4 | [-1.2, +0.4] | 500 |
| matchup: st19 vs st17 | -8.8 | [-13.7, -3.9] | 500 |
| matchup: st19 vs st18 | +6.0 | [+1.3, +10.7] | 500 |
| matchup: st19 vs st19 | -12.4 | [-18.1, -6.7] | 500 |
| matchup: st14 vs st04 | +6.2 | [+1.8, +10.6] | 500 |
| matchup: st14 vs st05 | +1.8 | [-3.5, +7.1] | 500 |
| matchup: st14 vs st06 | +11.6 | [+6.2, +17.0] | 500 |
| matchup: st14 vs st07 | +7.8 | [+2.3, +13.3] | 500 |
| matchup: st14 vs st08 | +14.2 | [+9.3, +19.1] | 500 |
| matchup: st14 vs st09 | +26.6 | [+21.0, +32.2] | 500 |
| matchup: st14 vs st10 | +3.0 | [-2.5, +8.5] | 500 |
| matchup: st14 vs st11 | +19.2 | [+13.6, +24.8] | 500 |
| matchup: st14 vs st12 | +4.0 | [-1.4, +9.4] | 500 |
| matchup: st14 vs st13 | +15.4 | [+10.0, +20.8] | 500 |
| matchup: st17 vs st18 | +2.8 | [+0.4, +5.2] | 500 |
| matchup: st17 vs st19 | -9.4 | [-14.2, -4.6] | 500 |
| matchup: st17 vs st20 | -3.0 | [-7.8, +1.8] | 500 |
| matchup: st17 vs st21 | -0.8 | [-5.0, +3.4] | 500 |
| matchup: st17 vs st22 | +9.2 | [+4.1, +14.3] | 500 |
| matchup: st17 vs st23 | +1.2 | [-2.0, +4.4] | 500 |
| matchup: st17 vs st24 | +3.2 | [-1.9, +8.3] | 500 |
| matchup: st17 vs st25 | +11.2 | [+7.7, +14.7] | 500 |
| matchup: st17 vs st26 | +12.4 | [+7.2, +17.6] | 500 |
| matchup: st17 vs st27 | +8.0 | [+2.8, +13.2] | 500 |
| matchup: st17 vs st28 | +4.2 | [-0.8, +9.2] | 500 |
| matchup: st17 vs st29 | -1.8 | [-5.0, +1.4] | 500 |
| matchup: st17 vs st30 | +0.0 | [+0.0, +0.0] | 500 |
| matchup: st18 vs lt01luffy | +23.8 | [+18.5, +29.1] | 500 |
| matchup: st18 vs lt01nami | +20.4 | [+14.8, +26.0] | 500 |
| matchup: st18 vs lt01zoro | +22.0 | [+17.0, +27.0] | 500 |
| matchup: st18 vs st01 | +7.8 | [+3.3, +12.3] | 500 |
| matchup: st18 vs st02 | +17.6 | [+13.2, +22.0] | 500 |
| matchup: st18 vs st03 | +2.0 | [+0.8, +3.2] | 500 |
| matchup: st18 vs st04 | +8.2 | [+4.8, +11.6] | 500 |
| matchup: st18 vs st05 | +10.8 | [+6.2, +15.4] | 500 |
| matchup: st18 vs st06 | +24.8 | [+19.6, +30.0] | 500 |
| matchup: st18 vs st07 | +14.6 | [+9.4, +19.8] | 500 |
| matchup: lt01zoro vs st12 | +7.0 | [+2.2, +11.8] | 500 |
| matchup: lt01zoro vs st13 | +25.6 | [+20.3, +30.9] | 500 |
| matchup: lt01zoro vs st14 | +27.8 | [+22.2, +33.4] | 500 |
| matchup: lt01zoro vs st15 | +29.8 | [+24.7, +34.9] | 500 |
| matchup: lt01zoro vs st16 | +0.0 | [+0.0, +0.0] | 500 |
| matchup: lt01zoro vs st17 | +5.6 | [+0.8, +10.4] | 500 |
| matchup: lt01zoro vs st18 | +25.6 | [+20.1, +31.1] | 500 |
| matchup: lt01zoro vs st19 | -1.6 | [-7.2, +4.0] | 500 |
| matchup: lt01zoro vs st20 | -1.8 | [-6.7, +3.1] | 500 |
| matchup: lt01zoro vs st21 | +7.2 | [+1.9, +12.5] | 500 |
| matchup: st16 vs st28 | +0.2 | [-0.5, +0.9] | 500 |
| matchup: st16 vs st29 | +0.2 | [-0.2, +0.6] | 500 |
| matchup: st16 vs st30 | +0.0 | [+0.0, +0.0] | 500 |
| matchup: st17 vs lt01luffy | -1.6 | [-3.8, +0.6] | 500 |
| matchup: st17 vs lt01nami | +0.4 | [-4.8, +5.6] | 500 |
| matchup: st17 vs lt01zoro | +6.4 | [+1.9, +10.9] | 500 |
| matchup: st17 vs st01 | -2.0 | [-7.1, +3.1] | 500 |
| matchup: st17 vs st02 | +1.8 | [-3.7, +7.3] | 500 |
| matchup: st17 vs st03 | +0.4 | [-3.1, +3.9] | 500 |
| matchup: st17 vs st04 | +0.4 | [-4.6, +5.4] | 500 |
| matchup: st17 vs st05 | -4.4 | [-9.8, +1.0] | 500 |
| matchup: st14 vs st26 | +10.4 | [+4.8, +16.0] | 500 |
| matchup: st14 vs st27 | +19.4 | [+13.6, +25.2] | 500 |
| matchup: st14 vs st28 | +9.2 | [+4.3, +14.1] | 500 |
| matchup: st14 vs st29 | +6.0 | [+0.5, +11.5] | 500 |
| matchup: st14 vs st30 | +0.0 | [+0.0, +0.0] | 500 |
| matchup: st15 vs lt01luffy | +11.8 | [+6.4, +17.2] | 500 |
| matchup: st15 vs lt01nami | +1.2 | [-3.8, +6.2] | 500 |
| matchup: st15 vs lt01zoro | +26.6 | [+21.7, +31.5] | 500 |
| matchup: st15 vs st01 | +4.8 | [+0.8, +8.8] | 500 |
| matchup: st15 vs st02 | +14.4 | [+10.0, +18.8] | 500 |
| matchup: st15 vs st03 | +1.6 | [+0.2, +3.0] | 500 |
| matchup: st15 vs st04 | +3.2 | [+0.2, +6.2] | 500 |
| matchup: st15 vs st05 | +2.6 | [-1.5, +6.7] | 500 |
| matchup: st15 vs st06 | +19.4 | [+14.4, +24.4] | 500 |
| matchup: st15 vs st07 | +9.2 | [+5.4, +13.0] | 500 |
| matchup: st15 vs st08 | +12.4 | [+8.8, +16.0] | 500 |
| matchup: st15 vs st09 | +14.4 | [+9.6, +19.2] | 500 |
| matchup: st15 vs st10 | +7.0 | [+2.9, +11.1] | 500 |
| matchup: st15 vs st11 | +9.8 | [+5.0, +14.6] | 500 |
| matchup: st15 vs st12 | +4.2 | [+1.5, +6.9] | 500 |
| matchup: st15 vs st13 | +7.0 | [+4.0, +10.0] | 500 |
| matchup: st15 vs st14 | +25.6 | [+20.2, +31.0] | 500 |
| matchup: st15 vs st15 | +20.4 | [+15.3, +25.5] | 500 |
| matchup: st11 vs st12 | +3.4 | [-2.0, +8.8] | 500 |
| matchup: st11 vs st13 | +24.0 | [+18.8, +29.2] | 500 |
| matchup: st11 vs st14 | +19.2 | [+14.0, +24.4] | 500 |
| matchup: st11 vs st15 | +16.2 | [+11.3, +21.1] | 500 |
| matchup: st11 vs st16 | -0.6 | [-1.6, +0.4] | 500 |
| matchup: st11 vs st17 | +3.4 | [-1.5, +8.3] | 500 |
| matchup: st11 vs st18 | +20.6 | [+15.9, +25.3] | 500 |
| matchup: st11 vs st19 | +9.8 | [+4.8, +14.8] | 500 |
| matchup: st11 vs st20 | +5.4 | [+0.2, +10.6] | 500 |
| matchup: st11 vs st21 | +5.0 | [+0.3, +9.7] | 500 |
| matchup: st05 vs st06 | +2.2 | [-3.0, +7.4] | 500 |
| matchup: st05 vs st07 | +0.2 | [-5.1, +5.5] | 500 |
| matchup: st05 vs st08 | +4.6 | [-0.7, +9.9] | 500 |
| matchup: st05 vs st09 | +4.0 | [-1.6, +9.6] | 500 |
| matchup: st05 vs st10 | +0.6 | [-4.9, +6.1] | 500 |
| matchup: st05 vs st11 | +6.8 | [+1.8, +11.8] | 500 |
| matchup: st05 vs st12 | -1.8 | [-7.3, +3.7] | 500 |
| matchup: st05 vs st13 | +13.4 | [+7.6, +19.2] | 500 |
| matchup: st05 vs st14 | +3.0 | [-2.1, +8.1] | 500 |
| matchup: st05 vs st15 | +2.6 | [-1.8, +7.0] | 500 |
| matchup: st05 vs st16 | -1.2 | [-2.4, +0.0] | 500 |
| matchup: st05 vs st17 | -6.6 | [-12.1, -1.1] | 500 |
| matchup: st05 vs st18 | +10.0 | [+5.1, +14.9] | 500 |
| matchup: st05 vs st19 | -6.2 | [-11.7, -0.7] | 500 |
| matchup: st05 vs st20 | -5.8 | [-10.9, -0.7] | 500 |
| matchup: st05 vs st21 | +1.0 | [-3.8, +5.8] | 500 |
| matchup: st05 vs st22 | +3.2 | [-2.1, +8.5] | 500 |
| matchup: st05 vs st23 | -9.6 | [-14.0, -5.2] | 500 |
| matchup: st05 vs st24 | +9.2 | [+3.7, +14.7] | 500 |
| matchup: st05 vs st25 | +25.4 | [+20.1, +30.7] | 500 |
| matchup: st05 vs st26 | +4.2 | [-1.3, +9.7] | 500 |
| matchup: st05 vs st27 | +3.6 | [-2.0, +9.2] | 500 |
| matchup: st08 vs st20 | +6.6 | [+2.0, +11.2] | 500 |
| matchup: st08 vs st21 | +1.8 | [-2.3, +5.9] | 500 |
| matchup: st08 vs st22 | +21.6 | [+16.5, +26.7] | 500 |
| matchup: st08 vs st23 | +2.0 | [-1.8, +5.8] | 500 |
| matchup: st08 vs st24 | +16.6 | [+11.4, +21.8] | 500 |
| matchup: st08 vs st25 | +30.4 | [+25.3, +35.5] | 500 |
| matchup: st08 vs st26 | +10.2 | [+5.3, +15.1] | 500 |
| matchup: st08 vs st27 | +21.4 | [+16.2, +26.6] | 500 |
| matchup: st08 vs st28 | +5.6 | [+1.4, +9.8] | 500 |
| matchup: st08 vs st29 | +4.0 | [+0.4, +7.6] | 500 |
| matchup: st08 vs st30 | +0.0 | [+0.0, +0.0] | 500 |
| matchup: st29 vs st30 | -0.2 | [-0.6, +0.2] | 500 |
| matchup: st30 vs lt01luffy | -0.2 | [-0.6, +0.2] | 500 |
| matchup: st30 vs lt01nami | +0.0 | [+0.0, +0.0] | 500 |
| matchup: st30 vs lt01zoro | +0.0 | [+0.0, +0.0] | 500 |
| matchup: st30 vs st01 | +0.0 | [+0.0, +0.0] | 500 |
| matchup: st30 vs st02 | +0.0 | [+0.0, +0.0] | 500 |
| matchup: st30 vs st03 | +0.0 | [+0.0, +0.0] | 500 |
| matchup: st30 vs st04 | +0.0 | [+0.0, +0.0] | 500 |
| matchup: st30 vs st05 | -0.8 | [-1.8, +0.2] | 500 |
| matchup: st30 vs st06 | +0.0 | [+0.0, +0.0] | 500 |
| matchup: st30 vs st07 | +0.0 | [+0.0, +0.0] | 500 |
| matchup: st30 vs st08 | +0.0 | [+0.0, +0.0] | 500 |
| matchup: st30 vs st09 | +0.0 | [+0.0, +0.0] | 500 |
| matchup: st30 vs st10 | +0.0 | [+0.0, +0.0] | 500 |
| matchup: st30 vs st11 | +0.0 | [+0.0, +0.0] | 500 |
| matchup: st30 vs st12 | +0.0 | [+0.0, +0.0] | 500 |
| matchup: st30 vs st13 | +0.0 | [+0.0, +0.0] | 500 |
| matchup: st30 vs st14 | +0.0 | [+0.0, +0.0] | 500 |
| matchup: st30 vs st15 | +0.0 | [+0.0, +0.0] | 500 |
| matchup: st30 vs st16 | +0.0 | [+0.0, +0.0] | 500 |
| matchup: st30 vs st17 | +0.0 | [+0.0, +0.0] | 500 |
| matchup: st30 vs st18 | +0.0 | [+0.0, +0.0] | 500 |
| matchup: st30 vs st19 | +0.2 | [-0.5, +0.9] | 500 |
| matchup: st27 vs st06 | +16.8 | [+11.5, +22.1] | 500 |
| matchup: st27 vs st07 | +6.4 | [+1.2, +11.6] | 500 |
| matchup: st27 vs st08 | +20.2 | [+14.9, +25.5] | 500 |
| matchup: st27 vs st09 | +23.0 | [+17.6, +28.4] | 500 |
| matchup: st27 vs st10 | +10.8 | [+5.4, +16.2] | 500 |
| matchup: st27 vs st11 | +22.6 | [+17.3, +27.9] | 500 |
| matchup: st27 vs st12 | +9.8 | [+4.3, +15.3] | 500 |
| matchup: st27 vs st13 | +26.4 | [+20.8, +32.0] | 500 |
| matchup: st27 vs st14 | +16.0 | [+10.4, +21.6] | 500 |
| matchup: st27 vs st15 | +14.6 | [+10.3, +18.9] | 500 |
| matchup: st27 vs st16 | +0.6 | [-0.4, +1.6] | 500 |
| matchup: st27 vs st17 | +3.8 | [-1.7, +9.3] | 500 |
| matchup: st27 vs st18 | +22.0 | [+17.4, +26.6] | 500 |
| matchup: st27 vs st19 | +5.4 | [-0.3, +11.1] | 500 |
| matchup: st27 vs st20 | -2.4 | [-7.3, +2.5] | 500 |
| matchup: st27 vs st21 | +3.6 | [-1.3, +8.5] | 500 |
| matchup: st27 vs st22 | +15.6 | [+10.2, +21.0] | 500 |
| matchup: st27 vs st23 | +4.0 | [-0.4, +8.4] | 500 |
| matchup: st27 vs st24 | +15.4 | [+10.1, +20.7] | 500 |
| matchup: st27 vs st25 | +32.0 | [+26.7, +37.3] | 500 |
| matchup: st27 vs st26 | +18.4 | [+13.4, +23.4] | 500 |
| matchup: st27 vs st27 | +20.2 | [+14.7, +25.7] | 500 |
| matchup: st29 vs st08 | +4.4 | [+0.7, +8.1] | 500 |
| matchup: st29 vs st09 | +3.4 | [-0.9, +7.7] | 500 |
| matchup: st29 vs st10 | +1.2 | [-3.5, +5.9] | 500 |
| matchup: st29 vs st11 | +8.2 | [+3.7, +12.7] | 500 |
| matchup: st29 vs st12 | +0.2 | [-4.0, +4.4] | 500 |
| matchup: st29 vs st13 | +14.2 | [+9.2, +19.2] | 500 |
| matchup: st29 vs st14 | -0.2 | [-5.1, +4.7] | 500 |
| matchup: st29 vs st15 | +0.4 | [-4.8, +5.6] | 500 |
| matchup: st29 vs st16 | +0.4 | [-0.2, +1.0] | 500 |
| matchup: st29 vs st17 | +0.2 | [-3.2, +3.6] | 500 |
| matchup: st07 vs st08 | +8.4 | [+3.5, +13.3] | 500 |
| matchup: st07 vs st09 | +7.0 | [+1.9, +12.1] | 500 |
| matchup: st07 vs st10 | +6.2 | [+0.9, +11.5] | 500 |
| matchup: st07 vs st11 | +1.2 | [-4.0, +6.4] | 500 |
| matchup: st07 vs st12 | -0.6 | [-5.5, +4.3] | 500 |
| matchup: st07 vs st13 | +14.0 | [+8.9, +19.1] | 500 |
| matchup: st07 vs st14 | +3.4 | [-2.1, +8.9] | 500 |
| matchup: st07 vs st15 | +9.4 | [+5.6, +13.2] | 500 |
| matchup: st07 vs st16 | -0.2 | [-1.1, +0.7] | 500 |
| matchup: st07 vs st17 | -1.8 | [-6.5, +2.9] | 500 |
| matchup: st04 vs st16 | +0.4 | [-1.3, +2.1] | 500 |
| matchup: st04 vs st17 | -5.8 | [-10.6, -1.0] | 500 |
| matchup: st04 vs st18 | +11.2 | [+7.5, +14.9] | 500 |
| matchup: st04 vs st19 | -3.6 | [-8.0, +0.8] | 500 |
| matchup: st04 vs st20 | -2.2 | [-6.5, +2.1] | 500 |
| matchup: st04 vs st21 | +1.6 | [-2.5, +5.7] | 500 |
| matchup: st04 vs st22 | +13.6 | [+8.8, +18.4] | 500 |
| matchup: st04 vs st23 | +0.8 | [-1.9, +3.5] | 500 |
| matchup: st04 vs st24 | +8.4 | [+3.5, +13.3] | 500 |
| matchup: st04 vs st25 | +22.2 | [+17.6, +26.8] | 500 |
| matchup: st21 vs lt01zoro | +9.2 | [+4.1, +14.3] | 500 |
| matchup: st21 vs st01 | -1.6 | [-6.2, +3.0] | 500 |
| matchup: st21 vs st02 | +1.4 | [-3.2, +6.0] | 500 |
| matchup: st21 vs st03 | +1.0 | [-1.0, +3.0] | 500 |
| matchup: st21 vs st04 | -0.2 | [-4.0, +3.6] | 500 |
| matchup: st21 vs st05 | -4.8 | [-9.9, +0.3] | 500 |
| matchup: st21 vs st06 | +2.0 | [-3.1, +7.1] | 500 |
| matchup: st21 vs st07 | -3.6 | [-8.6, +1.4] | 500 |
| matchup: st21 vs st08 | +8.6 | [+4.2, +13.0] | 500 |
| matchup: st21 vs st09 | +5.6 | [+0.9, +10.3] | 500 |
| matchup: st03 vs st04 | -0.2 | [-3.7, +3.3] | 500 |
| matchup: st03 vs st05 | -2.2 | [-5.4, +1.0] | 500 |
| matchup: st03 vs st06 | +0.8 | [-1.5, +3.1] | 500 |
| matchup: st03 vs st07 | +1.6 | [-1.2, +4.4] | 500 |
| matchup: st03 vs st08 | +1.4 | [-1.8, +4.6] | 500 |
| matchup: st03 vs st09 | +0.8 | [-2.2, +3.8] | 500 |
| matchup: st03 vs st10 | -1.2 | [-4.6, +2.2] | 500 |
| matchup: st03 vs st11 | +4.8 | [+1.6, +8.0] | 500 |
| matchup: st03 vs st12 | +3.0 | [-0.5, +6.5] | 500 |
| matchup: st03 vs st13 | +3.6 | [-0.6, +7.8] | 500 |
| matchup: st22 vs st12 | +11.8 | [+6.4, +17.2] | 500 |
| matchup: st22 vs st13 | +22.4 | [+16.9, +27.9] | 500 |
| matchup: st22 vs st14 | +12.8 | [+7.5, +18.1] | 500 |
| matchup: st22 vs st15 | +11.8 | [+8.2, +15.4] | 500 |
| matchup: st22 vs st16 | -0.2 | [-1.8, +1.4] | 500 |
| matchup: st22 vs st17 | +8.2 | [+3.1, +13.3] | 500 |
| matchup: st22 vs st18 | +19.4 | [+14.9, +23.9] | 500 |
| matchup: st22 vs st19 | +5.8 | [+0.3, +11.3] | 500 |
| matchup: st22 vs st20 | +5.0 | [+0.5, +9.5] | 500 |
| matchup: st22 vs st21 | +6.8 | [+2.2, +11.4] | 500 |
| matchup: st01 vs st24 | +6.0 | [+1.1, +10.9] | 500 |
| matchup: st01 vs st25 | +24.2 | [+19.0, +29.4] | 500 |
| matchup: st01 vs st26 | -3.4 | [-8.4, +1.6] | 500 |
| matchup: st01 vs st27 | +4.2 | [-0.8, +9.2] | 500 |
| matchup: st01 vs st28 | +2.4 | [-2.8, +7.6] | 500 |
| matchup: st01 vs st29 | -2.0 | [-6.4, +2.4] | 500 |
| matchup: st01 vs st30 | +0.0 | [+0.0, +0.0] | 500 |
| matchup: st02 vs lt01luffy | +0.0 | [-3.0, +3.0] | 500 |
| matchup: st02 vs lt01nami | -1.6 | [-7.5, +4.3] | 500 |
| matchup: st02 vs lt01zoro | +13.6 | [+8.4, +18.8] | 500 |
| matchup: st02 vs st01 | +1.8 | [-3.4, +7.0] | 500 |
| matchup: st12 vs st24 | +4.0 | [-1.3, +9.3] | 500 |
| matchup: st12 vs st25 | +22.8 | [+18.0, +27.6] | 500 |
| matchup: st12 vs st26 | +5.6 | [+0.0, +11.2] | 500 |
| matchup: st12 vs st27 | +8.4 | [+2.9, +13.9] | 500 |
| matchup: st12 vs st28 | +4.4 | [-1.1, +9.9] | 500 |
| matchup: st12 vs st29 | -0.4 | [-4.7, +3.9] | 500 |
| matchup: st12 vs st30 | +0.0 | [+0.0, +0.0] | 500 |
| matchup: st13 vs lt01luffy | +11.0 | [+7.3, +14.7] | 500 |
| matchup: st13 vs lt01nami | +10.4 | [+4.6, +16.2] | 500 |
| matchup: st13 vs lt01zoro | +27.8 | [+22.1, +33.5] | 500 |
| matchup: st13 vs st01 | +9.2 | [+4.2, +14.2] | 500 |
| matchup: lt01luffy vs lt01luffy | +16.8 | [+11.2, +22.4] | 500 |
| matchup: lt01luffy vs lt01nami | +5.4 | [+0.3, +10.5] | 500 |
| matchup: lt01luffy vs lt01zoro | +12.2 | [+8.5, +15.9] | 500 |
| matchup: lt01luffy vs st01 | -2.0 | [-5.8, +1.8] | 500 |
| matchup: lt01luffy vs st02 | +4.8 | [+1.8, +7.8] | 500 |
| matchup: lt01luffy vs st03 | +0.6 | [-0.3, +1.5] | 500 |
| matchup: lt01luffy vs st04 | +2.2 | [-0.5, +4.9] | 500 |
| matchup: lt01luffy vs st05 | +1.4 | [-2.1, +4.9] | 500 |
| matchup: lt01luffy vs st06 | +9.6 | [+5.0, +14.2] | 500 |
| matchup: lt01luffy vs st07 | +2.4 | [-2.1, +6.9] | 500 |
| matchup: lt01luffy vs st08 | +8.0 | [+4.9, +11.1] | 500 |
| matchup: lt01luffy vs st09 | +6.0 | [+3.0, +9.0] | 500 |
| matchup: lt01luffy vs st10 | +6.4 | [+2.9, +9.9] | 500 |
| matchup: lt01luffy vs st11 | +11.6 | [+8.0, +15.2] | 500 |
| matchup: lt01luffy vs st12 | -0.6 | [-3.0, +1.8] | 500 |
| matchup: lt01luffy vs st13 | +14.2 | [+10.0, +18.4] | 500 |
| matchup: lt01luffy vs st14 | +10.6 | [+6.3, +14.9] | 500 |
| matchup: lt01luffy vs st15 | +12.0 | [+6.7, +17.3] | 500 |
| matchup: lt01luffy vs st16 | +0.0 | [+0.0, +0.0] | 500 |
| matchup: lt01luffy vs st17 | +0.0 | [-2.1, +2.1] | 500 |
| matchup: lt01luffy vs st18 | +23.0 | [+17.8, +28.2] | 500 |
| matchup: lt01luffy vs st19 | +0.6 | [-2.5, +3.7] | 500 |
| matchup: lt01luffy vs st20 | +0.8 | [-3.7, +5.3] | 500 |
| matchup: lt01luffy vs st21 | -0.8 | [-5.8, +4.2] | 500 |
| matchup: lt01luffy vs st22 | +6.2 | [+2.6, +9.8] | 500 |
| matchup: lt01luffy vs st23 | -11.4 | [-16.6, -6.2] | 500 |
| matchup: lt01luffy vs st24 | +8.4 | [+5.0, +11.8] | 500 |
| matchup: lt01luffy vs st25 | +18.0 | [+13.3, +22.7] | 500 |
| matchup: lt01luffy vs st26 | +9.4 | [+4.7, +14.1] | 500 |
| matchup: lt01luffy vs st27 | +6.0 | [+3.0, +9.0] | 500 |
| matchup: lt01luffy vs st28 | +8.0 | [+3.0, +13.0] | 500 |
| matchup: lt01luffy vs st29 | +1.4 | [-4.0, +6.8] | 500 |
| matchup: lt01luffy vs st30 | +0.2 | [-0.2, +0.6] | 500 |
| matchup: lt01nami vs lt01luffy | +3.4 | [-1.3, +8.1] | 500 |
| matchup: lt01nami vs lt01nami | +5.8 | [-0.4, +12.0] | 500 |
| matchup: lt01nami vs lt01zoro | +22.8 | [+17.4, +28.2] | 500 |
| matchup: lt01nami vs st01 | -4.2 | [-9.5, +1.1] | 500 |
| matchup: lt01nami vs st02 | +2.2 | [-3.4, +7.8] | 500 |
| matchup: lt01nami vs st03 | +0.4 | [-2.5, +3.3] | 500 |
| matchup: lt01nami vs st04 | +3.2 | [-1.7, +8.1] | 500 |
| matchup: lt01nami vs st05 | -4.6 | [-10.1, +0.9] | 500 |
| matchup: lt01nami vs st06 | +4.4 | [-1.3, +10.1] | 500 |
| matchup: lt01nami vs st07 | +6.0 | [+0.6, +11.4] | 500 |
| matchup: lt01nami vs st08 | +9.8 | [+4.7, +14.9] | 500 |
| matchup: lt01nami vs st09 | +6.6 | [+1.0, +12.2] | 500 |
| matchup: st19 vs st20 | -12.2 | [-17.3, -7.1] | 500 |
| matchup: st19 vs st21 | +0.0 | [-5.1, +5.1] | 500 |
| matchup: st19 vs st22 | +10.4 | [+5.1, +15.7] | 500 |
| matchup: st19 vs st23 | -6.6 | [-11.6, -1.6] | 500 |
| matchup: st19 vs st24 | +3.4 | [-2.2, +9.0] | 500 |
| matchup: st19 vs st25 | +20.4 | [+15.3, +25.5] | 500 |
| matchup: st19 vs st26 | +3.8 | [-1.7, +9.3] | 500 |
| matchup: st19 vs st27 | +9.0 | [+3.3, +14.7] | 500 |
| matchup: st19 vs st28 | +0.6 | [-5.2, +6.4] | 500 |
| matchup: st19 vs st29 | -7.4 | [-11.9, -2.9] | 500 |
| matchup: st19 vs st30 | +0.4 | [-0.2, +1.0] | 500 |
| matchup: st27 vs st28 | +9.8 | [+4.7, +14.9] | 500 |
| matchup: st27 vs st29 | +3.2 | [-1.4, +7.8] | 500 |
| matchup: st27 vs st30 | +0.0 | [+0.0, +0.0] | 500 |
| matchup: st28 vs lt01luffy | +13.0 | [+7.8, +18.2] | 500 |
| matchup: st28 vs lt01nami | +2.2 | [-3.1, +7.5] | 500 |
| matchup: st28 vs lt01zoro | +11.0 | [+5.7, +16.3] | 500 |
| matchup: st28 vs st01 | +8.2 | [+2.9, +13.5] | 500 |
| matchup: st28 vs st02 | +8.8 | [+3.6, +14.0] | 500 |
| matchup: st28 vs st03 | -1.4 | [-4.1, +1.3] | 500 |
| matchup: st28 vs st04 | +4.6 | [+0.3, +8.9] | 500 |
| matchup: st28 vs st05 | +6.2 | [+1.3, +11.1] | 500 |
| matchup: st26 vs st15 | +9.2 | [+5.1, +13.3] | 500 |
| matchup: st26 vs st16 | +0.4 | [-0.7, +1.5] | 500 |
| matchup: st26 vs st17 | +11.8 | [+6.8, +16.8] | 500 |
| matchup: st26 vs st18 | +22.6 | [+17.0, +28.2] | 500 |
| matchup: st26 vs st19 | +0.2 | [-5.4, +5.8] | 500 |
| matchup: st26 vs st20 | +0.6 | [-4.6, +5.8] | 500 |
| matchup: st26 vs st21 | +6.6 | [+1.5, +11.7] | 500 |
| matchup: st26 vs st22 | +14.6 | [+9.1, +20.1] | 500 |
| matchup: st26 vs st23 | +3.6 | [-1.4, +8.6] | 500 |
| matchup: st26 vs st24 | +3.8 | [-1.9, +9.5] | 500 |
| matchup: st26 vs st25 | +34.0 | [+28.7, +39.3] | 500 |
| matchup: st18 vs st08 | +20.2 | [+16.1, +24.3] | 500 |
| matchup: st18 vs st09 | +26.6 | [+22.0, +31.2] | 500 |
| matchup: st18 vs st10 | +17.8 | [+13.0, +22.6] | 500 |
| matchup: st18 vs st11 | +22.6 | [+18.0, +27.2] | 500 |
| matchup: st18 vs st12 | +3.6 | [+0.5, +6.7] | 500 |
| matchup: st18 vs st13 | +26.6 | [+21.6, +31.6] | 500 |
| matchup: st18 vs st14 | +18.8 | [+13.4, +24.2] | 500 |
| matchup: st18 vs st15 | +24.0 | [+18.6, +29.4] | 500 |
| matchup: st18 vs st16 | +0.2 | [-0.2, +0.6] | 500 |
| matchup: st18 vs st17 | +2.6 | [+0.6, +4.6] | 500 |
| matchup: st05 vs st28 | -1.6 | [-6.4, +3.2] | 500 |
| matchup: st05 vs st29 | -1.2 | [-5.5, +3.1] | 500 |
| matchup: st05 vs st30 | +0.4 | [-0.2, +1.0] | 500 |
| matchup: st06 vs lt01luffy | +10.8 | [+6.5, +15.1] | 500 |
| matchup: st06 vs lt01nami | -3.6 | [-9.1, +1.9] | 500 |
| matchup: st06 vs lt01zoro | +19.4 | [+14.1, +24.7] | 500 |
| matchup: st06 vs st01 | +1.4 | [-3.8, +6.6] | 500 |
| matchup: st06 vs st02 | +12.4 | [+7.0, +17.8] | 500 |
| matchup: st06 vs st03 | +0.4 | [-1.8, +2.6] | 500 |
| matchup: st06 vs st04 | +10.2 | [+6.0, +14.4] | 500 |
| matchup: st06 vs st05 | +7.4 | [+2.4, +12.4] | 500 |
| matchup: st24 vs st14 | +15.6 | [+10.6, +20.6] | 500 |
| matchup: st24 vs st15 | +12.4 | [+7.4, +17.4] | 500 |
| matchup: st24 vs st16 | -0.2 | [-0.6, +0.2] | 500 |
| matchup: st24 vs st17 | +3.6 | [-1.5, +8.7] | 500 |
| matchup: st24 vs st18 | +14.4 | [+10.6, +18.2] | 500 |
| matchup: st24 vs st19 | +1.2 | [-4.1, +6.5] | 500 |
| matchup: st24 vs st20 | +5.4 | [+0.3, +10.5] | 500 |
| matchup: st24 vs st21 | +0.8 | [-3.6, +5.2] | 500 |
| matchup: st24 vs st22 | +21.6 | [+16.2, +27.0] | 500 |
| matchup: st24 vs st23 | +3.4 | [-0.5, +7.3] | 500 |
| matchup: st24 vs st24 | +19.8 | [+14.3, +25.3] | 500 |
| matchup: st24 vs st25 | +27.8 | [+22.7, +32.9] | 500 |
| matchup: st24 vs st26 | +11.0 | [+5.7, +16.3] | 500 |
| matchup: st24 vs st27 | +15.4 | [+10.4, +20.4] | 500 |
| matchup: st24 vs st28 | +18.6 | [+13.7, +23.5] | 500 |
| matchup: st24 vs st29 | +4.2 | [+0.1, +8.3] | 500 |
| matchup: st24 vs st30 | +0.0 | [+0.0, +0.0] | 500 |
| matchup: st25 vs lt01luffy | +17.4 | [+13.0, +21.8] | 500 |
| matchup: st25 vs lt01nami | +31.6 | [+26.1, +37.1] | 500 |
| matchup: st25 vs lt01zoro | +31.2 | [+25.7, +36.7] | 500 |
| matchup: st25 vs st01 | +20.8 | [+15.6, +26.0] | 500 |
| matchup: st25 vs st02 | +17.2 | [+11.9, +22.5] | 500 |
| matchup: st25 vs st03 | +11.2 | [+8.0, +14.4] | 500 |
| matchup: st23 vs st24 | +0.0 | [-4.0, +4.0] | 500 |
| matchup: st23 vs st25 | +11.6 | [+6.9, +16.3] | 500 |
| matchup: st23 vs st26 | +8.0 | [+3.1, +12.9] | 500 |
| matchup: st23 vs st27 | +6.6 | [+2.5, +10.7] | 500 |
| matchup: st23 vs st28 | -1.0 | [-6.6, +4.6] | 500 |
| matchup: st23 vs st29 | -5.8 | [-11.4, -0.2] | 500 |
| matchup: st23 vs st30 | +0.0 | [+0.0, +0.0] | 500 |
| matchup: st24 vs lt01luffy | +8.0 | [+4.5, +11.5] | 500 |
| matchup: st24 vs lt01nami | +4.4 | [-0.9, +9.7] | 500 |
| matchup: st24 vs lt01zoro | +13.8 | [+8.5, +19.1] | 500 |
| matchup: st24 vs st01 | +10.4 | [+5.2, +15.6] | 500 |
| matchup: st25 vs st26 | +28.4 | [+22.9, +33.9] | 500 |
| matchup: st25 vs st27 | +34.4 | [+29.4, +39.4] | 500 |
| matchup: st25 vs st28 | +16.8 | [+11.1, +22.5] | 500 |
| matchup: st25 vs st29 | +22.6 | [+17.4, +27.8] | 500 |
| matchup: st25 vs st30 | +0.0 | [+0.0, +0.0] | 500 |
| matchup: st26 vs lt01luffy | +6.8 | [+1.9, +11.7] | 500 |
| matchup: st26 vs lt01nami | -0.4 | [-5.7, +4.9] | 500 |
| matchup: st26 vs lt01zoro | +11.8 | [+6.5, +17.1] | 500 |
| matchup: st26 vs st01 | -1.4 | [-6.5, +3.7] | 500 |
| matchup: st26 vs st02 | +5.2 | [-0.2, +10.6] | 500 |
| matchup: st26 vs st03 | +1.2 | [-2.2, +4.6] | 500 |
| matchup: st26 vs st04 | +10.4 | [+5.2, +15.6] | 500 |
| matchup: st26 vs st05 | +2.0 | [-3.5, +7.5] | 500 |
| matchup: st26 vs st06 | +11.4 | [+6.4, +16.4] | 500 |
| matchup: st26 vs st07 | +12.0 | [+7.0, +17.0] | 500 |
| matchup: st26 vs st08 | +11.4 | [+6.3, +16.5] | 500 |
| matchup: st26 vs st09 | +17.0 | [+11.6, +22.4] | 500 |
| matchup: st26 vs st10 | +6.2 | [+0.6, +11.8] | 500 |
| matchup: st26 vs st11 | +8.0 | [+2.9, +13.1] | 500 |
| matchup: st26 vs st12 | +5.4 | [-0.1, +10.9] | 500 |
| matchup: st26 vs st13 | +21.2 | [+15.7, +26.7] | 500 |
| matchup: st26 vs st14 | +11.8 | [+6.4, +17.2] | 500 |
| matchup: st15 vs st16 | -0.6 | [-1.3, +0.1] | 500 |
| matchup: st15 vs st17 | +1.2 | [-1.8, +4.2] | 500 |
| matchup: st15 vs st18 | +17.8 | [+12.7, +22.9] | 500 |
| matchup: st15 vs st19 | +8.0 | [+2.5, +13.5] | 500 |
| matchup: st15 vs st20 | +6.4 | [+0.8, +12.0] | 500 |
| matchup: st15 vs st21 | -0.4 | [-5.4, +4.6] | 500 |
| matchup: st15 vs st22 | +9.4 | [+6.2, +12.6] | 500 |
| matchup: st15 vs st23 | +3.4 | [-1.9, +8.7] | 500 |
| matchup: st15 vs st24 | +17.8 | [+12.7, +22.9] | 500 |
| matchup: st15 vs st25 | +19.8 | [+14.7, +24.9] | 500 |
| matchup: st25 vs st04 | +20.6 | [+16.1, +25.1] | 500 |
| matchup: st25 vs st05 | +26.0 | [+20.7, +31.3] | 500 |
| matchup: st25 vs st06 | +22.4 | [+17.4, +27.4] | 500 |
| matchup: st25 vs st07 | +25.4 | [+20.3, +30.5] | 500 |
| matchup: st25 vs st08 | +29.8 | [+24.8, +34.8] | 500 |
| matchup: st25 vs st09 | +33.0 | [+28.0, +38.0] | 500 |
| matchup: st25 vs st10 | +30.4 | [+25.0, +35.8] | 500 |
| matchup: st25 vs st11 | +32.8 | [+27.9, +37.7] | 500 |
| matchup: st25 vs st12 | +18.8 | [+14.1, +23.5] | 500 |
| matchup: st25 vs st13 | +31.2 | [+25.9, +36.5] | 500 |
| matchup: st30 vs st20 | +0.0 | [+0.0, +0.0] | 500 |
| matchup: st30 vs st21 | +0.0 | [+0.0, +0.0] | 500 |
| matchup: st30 vs st22 | +0.0 | [+0.0, +0.0] | 500 |
| matchup: st30 vs st23 | +0.0 | [+0.0, +0.0] | 500 |
| matchup: st30 vs st24 | +0.0 | [+0.0, +0.0] | 500 |
| matchup: st30 vs st25 | +0.0 | [-0.6, +0.6] | 500 |
| matchup: st30 vs st26 | +0.0 | [+0.0, +0.0] | 500 |
| matchup: st30 vs st27 | +0.0 | [+0.0, +0.0] | 500 |
| matchup: st30 vs st28 | +0.0 | [+0.0, +0.0] | 500 |
| matchup: st30 vs st29 | +0.0 | [+0.0, +0.0] | 500 |
| matchup: st30 vs st30 | -5.8 | [-11.2, -0.4] | 500 |
| matchup: st14 vs st15 | +31.3 | [+26.1, +36.4] | 499 |
