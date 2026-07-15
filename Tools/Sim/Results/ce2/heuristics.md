# Heuristic report — counter-economy

**Question:** Should south CONSERVE its counter cards (vs defending with them)?  
**Effect measured:** P(south wins | **conserve**) − P(south wins | **defend**), in percentage points (pp), over matched-seed pairs. Positive ⇒ prefer *conserve*.

**Overall:** -25.0 pp  (95% CI [-40.3, -9.7], n=40)
_Promotion gate: n ≥ 5 paired trials AND the 95% CI excludes 0._

## Promoted findings (10)

**Finding:** prefer **defend** when `matchup: st01 vs st05`.  
- Effect: -50.0 pp win probability (choosing conserve over defend)  
- Validation: 10 paired trials, 95% CI [-82.7, -17.3] pp  
- Raw: conserve won 2, defend won 7  

**Finding:** prefer **defend** when `i_am_faster = true`.  
- Effect: -30.0 pp win probability (choosing conserve over defend)  
- Validation: 30 paired trials, 95% CI [-49.1, -10.9] pp  
- Raw: conserve won 4, defend won 13  

**Finding:** prefer **defend** when `my_counter_density = 0.6`.  
- Effect: -30.0 pp win probability (choosing conserve over defend)  
- Validation: 20 paired trials, 95% CI [-55.0, -5.0] pp  
- Raw: conserve won 4, defend won 10  

**Finding:** prefer **defend** when `matchup: st03 vs st05`.  
- Effect: -30.0 pp win probability (choosing conserve over defend)  
- Validation: 10 paired trials, 95% CI [-59.9, -0.1] pp  
- Raw: conserve won 0, defend won 3  

**Finding:** prefer **defend** when `opp_low_curve = 0.5`.  
- Effect: -25.0 pp win probability (choosing conserve over defend)  
- Validation: 40 paired trials, 95% CI [-40.3, -9.7] pp  
- Raw: conserve won 4, defend won 14  

**Finding:** prefer **defend** when `opp_leader_life = 5`.  
- Effect: -25.0 pp win probability (choosing conserve over defend)  
- Validation: 40 paired trials, 95% CI [-40.3, -9.7] pp  
- Raw: conserve won 4, defend won 14  

**Finding:** prefer **defend** when `opp_events = 6+`.  
- Effect: -25.0 pp win probability (choosing conserve over defend)  
- Validation: 40 paired trials, 95% CI [-40.3, -9.7] pp  
- Raw: conserve won 4, defend won 14  

**Finding:** prefer **defend** when `i_have_more_counters = 0`.  
- Effect: -25.0 pp win probability (choosing conserve over defend)  
- Validation: 40 paired trials, 95% CI [-40.3, -9.7] pp  
- Raw: conserve won 4, defend won 14  

**Finding:** prefer **defend** when `opp_many_blockers = 1`.  
- Effect: -25.0 pp win probability (choosing conserve over defend)  
- Validation: 40 paired trials, 95% CI [-40.3, -9.7] pp  
- Raw: conserve won 4, defend won 14  

**Finding:** prefer **defend** when `my_counter_density = 0.7`.  
- Effect: -20.0 pp win probability (choosing conserve over defend)  
- Validation: 20 paired trials, 95% CI [-38.0, -2.0] pp  
- Raw: conserve won 0, defend won 4  

## All feature buckets (ungated — includes reversals)

| condition | effect pp | 95% CI pp | n |
|---|---:|:---:|---:|
| opp_low_curve = 0.5 | -25.0 | [-40.3, -9.7] | 40 |
| opp_leader_life = 5 | -25.0 | [-40.3, -9.7] | 40 |
| opp_events = 6+ | -25.0 | [-40.3, -9.7] | 40 |
| i_have_more_counters = 0 | -25.0 | [-40.3, -9.7] | 40 |
| opp_many_blockers = 1 | -25.0 | [-40.3, -9.7] | 40 |
| i_am_faster = true | -30.0 | [-49.1, -10.9] | 30 |
| my_counter_density = 0.7 | -20.0 | [-38.0, -2.0] | 20 |
| my_counter_density = 0.6 | -30.0 | [-55.0, -5.0] | 20 |
| i_am_faster = false | -10.0 | [-29.6, +9.6] | 10 |
| matchup: st03 vs st02 | -10.0 | [-29.6, +9.6] | 10 |
| matchup: st01 vs st02 | -10.0 | [-45.2, +25.2] | 10 |
| matchup: st01 vs st05 | -50.0 | [-82.7, -17.3] | 10 |
| matchup: st03 vs st05 | -30.0 | [-59.9, -0.1] | 10 |
